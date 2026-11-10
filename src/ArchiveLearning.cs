using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
namespace ElymsPet {
 // Independently implemented mechanisms inspired by Mem0 and LangMem; see docs/archive-open-source.md.
 [DataContract] public sealed class ArchiveChange {
  [DataMember] public DateTime At;
  [DataMember] public string Action="";
  [DataMember] public ArchiveItem Before;
  [DataMember] public ArchiveItem After;
 }
 [DataContract] public sealed class ArchiveEpisode {
  [DataMember] public string Id="";
  [DataMember] public string Task="";
  [DataMember] public string Workflow="";
  [DataMember] public string Situation="";
  [DataMember] public string Decision="";
  [DataMember] public string Outcome="未验证";
  [DataMember] public string Lesson="";
  [DataMember] public string Evidence="";
  [DataMember] public string Source="访谈自述";
  [DataMember] public DateTime At;
 }
 public static class ArchiveLearning {
  public const string Schema="\n经验案例字段 episodes=[{task,workflow,situation,decision,outcome,lesson,evidence}]。只提取用户亲自描述的真实工作案例，分别记录场景、选择与理由、成功或失败结果及经验。outcome 只能是成功|失败|未验证；没有结果就写未验证。evidence 必须逐字引用一段用户原话（不要改写），不能引用助手推测或资料正文。workflow 为关联流程的原名，没有则留空；案例不是执行证明，不得因此自动确认流程。没有案例输出空数组。\n";
  static string Clean(string value,int max) {return ArchiveStore.Clip(ArchiveStore.Clean(value),max);}
  public static void Normalize(ArchiveSlot slot) {
   if(slot.Changes==null)slot.Changes=new List<ArchiveChange>();
   if(slot.Episodes==null)slot.Episodes=new List<ArchiveEpisode>();
   slot.Changes.RemoveAll(delegate(ArchiveChange x){return x==null;});
   slot.Episodes.RemoveAll(delegate(ArchiveEpisode e){return e==null||string.IsNullOrWhiteSpace(e.Task);});
   foreach(var e in slot.Episodes) {
    e.Task=Clean(e.Task,120);e.Workflow=Clean(e.Workflow,120);e.Situation=Clean(e.Situation,600);e.Decision=Clean(e.Decision,600);e.Lesson=Clean(e.Lesson,600);e.Evidence=Clean(e.Evidence,500);
    e.Outcome=Outcome(e.Outcome);e.Source=e.Source=="用户复盘"?e.Source:"访谈自述";
    if(string.IsNullOrEmpty(e.Id))e.Id=Guid.NewGuid().ToString("N");
   }
   while(slot.Changes.Count>200)slot.Changes.RemoveAt(0);
   while(slot.Episodes.Count>200)slot.Episodes.RemoveAt(0);
  }
  public static string Outcome(string value) {return value=="成功"||value=="失败"?value:"未验证";}
  public static ArchiveItem Copy(ArchiveItem x) {
   return x==null?null:new ArchiveItem{Id=x.Id,Domain=x.Domain,Title=x.Title,Level=x.Level,Boundary=x.Boundary,Evidence=x.Evidence,Note=x.Note,At=x.At,Source=x.Source};
  }
  public static string ItemText(ArchiveItem x) {return x==null?"（无）":x.Domain+" · "+x.Title+" · "+x.Boundary+" / "+x.Level+"\n依据："+x.Evidence+"\n备注："+x.Note;}
  public static bool Same(ArchiveItem a,ArchiveItem b) {return ItemText(a)==ItemText(b);}
  public static void Record(ArchiveSlot slot,string action,ArchiveItem before,ArchiveItem after) {
   Normalize(slot);slot.Changes.Add(new ArchiveChange{At=DateTime.UtcNow,Action=action,Before=Copy(before),After=Copy(after)});
   while(slot.Changes.Count>200)slot.Changes.RemoveAt(0);
  }
  public static void Parse(Dictionary<string,object> map,InterviewResult result) {
   var list=MiniJson.GetArray(map,"episodes");if(list==null)return;
   foreach(var value in list) {
    var e=value as Dictionary<string,object>;if(e==null)continue;
    result.Episodes.Add(new ArchiveEpisode{Task=MiniJson.GetString(e,"task"),Workflow=MiniJson.GetString(e,"workflow"),Situation=MiniJson.GetString(e,"situation"),Decision=MiniJson.GetString(e,"decision"),Outcome=MiniJson.GetString(e,"outcome"),Lesson=MiniJson.GetString(e,"lesson"),Evidence=MiniJson.GetString(e,"evidence")});
   }
  }
  public static void Merge(ArchiveSlot slot,List<ArchiveEpisode> episodes) {
   Normalize(slot);
   if(episodes==null)return;
   var incoming=new ArchiveSlot{Episodes=episodes};Normalize(incoming);
   foreach(var e in incoming.Episodes) {
    if(e==null||string.IsNullOrWhiteSpace(e.Task)||string.IsNullOrWhiteSpace(e.Evidence))continue;
    string quote=ArchiveStore.Clean(e.Evidence);ArchiveTurn source=null;
    for(int i=slot.Turns.Count-1;i>=0;i--)if(slot.Turns[i].Role=="answer"&&slot.Turns[i].Text.Contains(quote)){source=slot.Turns[i];break;}
    if(source==null)continue; // No original user evidence, no autobiographical memory.
    e.At=source.At;e.Source="访谈自述";
    bool duplicate=slot.Episodes.Exists(delegate(ArchiveEpisode x){return x.Task==e.Task&&x.Evidence==quote;});
    if(!duplicate)slot.Episodes.Add(e);
   }
   Normalize(slot);
  }
  public static bool Feedback(ArchiveSlot slot,ArchiveWorkflow workflow,string outcome,string note) {
   Normalize(slot);note=Clean(note,500);
   if(workflow==null||!slot.Workflows.Contains(workflow)||note.Length==0)return false;
   outcome=Outcome(outcome);
   slot.Episodes.Add(new ArchiveEpisode{Id=Guid.NewGuid().ToString("N"),Task=workflow.Title+" · 使用复盘",Workflow=workflow.Title,Situation=workflow.Trigger,Decision="使用当时的流程："+Clean(workflow.Steps,500),Outcome=outcome,Lesson=note,Evidence=note,Source="用户复盘",At=DateTime.UtcNow});
   if(outcome=="失败")workflow.Confirmed=false;
   slot.Updated=DateTime.UtcNow;Normalize(slot);return true;
  }
  public static string EpisodeText(ArchiveEpisode e) {
   return e.Task+"（"+e.Outcome+" · "+e.Source+"）\n场景："+e.Situation+"\n做法与判断："+e.Decision+"\n经验："+e.Lesson+"\n原话："+e.Evidence;
  }
  public static string Context(ArchiveSlot slot) {
   var text=new StringBuilder("\n从案例追问：根据协助任务的实际阻塞，选择同一主题尚未说明的场景、选择理由、结果、失败边界；每轮只补一个缺口，不重复已经回答的内容。失败案例用于修订流程草稿，不当成已掌握的证明。\n");
   int count=0;
   for(int i=slot.Episodes.Count-1;i>=0&&count<4;i--) {
    var e=slot.Episodes[i];
    if(slot.Focus.Length>0&&ArchiveRecall.Score(slot.Focus,e.Task+e.Workflow+e.Situation+e.Lesson)<=0)continue;
    text.Append(EpisodeText(e)).Append("\n");count++;
   }
   return text.ToString();
  }
  public static string Markdown(ArchiveSlot slot) {
   var text=new StringBuilder("# 经验案例与复盘\n\n成功与失败均来自用户自述，并非自动执行验证。\n\n");
   foreach(var e in slot.Episodes)text.Append("## ").Append(e.Task).Append("\n\n").Append(EpisodeText(e)).Append("\n记录（UTC）：").Append(e.At.ToString("u")).Append("\n\n");
   return text.ToString();
  }
  public static string History(ArchiveSlot slot) {
   var text=new StringBuilder("# 知识变更记录（最近 200 次）\n\n");
   foreach(var c in slot.Changes)text.Append("## ").Append(c.Action).Append(" · ").Append(c.At.ToString("u")).Append("\n\n之前：").Append(ItemText(c.Before)).Append("\n\n之后：").Append(ItemText(c.After)).Append("\n\n");
   return text.ToString();
  }
 }
}
