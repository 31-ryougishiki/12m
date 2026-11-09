using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Threading;

#pragma warning disable 0649
namespace ElymsPet {
 public sealed class AutonomyPlan {
  public string Type="rest",Topic="",Message="",Task="",Note="",Reason="";
 }
 [DataContract] sealed class AutonomyPlanData {
  [DataMember] public string type;
  [DataMember] public string topic;
  [DataMember] public string message;
  [DataMember] public string task;
  [DataMember] public string note;
  [DataMember] public string reason;
 }
 [DataContract] sealed class AutonomyEntry {
  [DataMember] public DateTime time;
  [DataMember] public string type;
  [DataMember] public string summary;
 }
 [DataContract] sealed class AutonomyJournal {
  [DataMember] public AutonomyEntry[] entries;
 }
 /// <summary>约每 30 分钟的自主时间：她自己决定聊天、逛 B站、写笔记，或（显式允许时）做一个小实验。</summary>
 public sealed class AutonomyService {
  readonly PetWindow pet;
  readonly DispatcherTimer timer;
  readonly List<AutonomyEntry> journal=new List<AutonomyEntry>();
  readonly Random random=new Random();
  DateTime nextAt=DateTime.MinValue;
  bool running,waitingBili,waitingDsh;
  string activity="",lastSummary="",lastType="";
  public event Action Changed;
  public string LastSummary {get {return lastSummary;}}
  public string LastType {get {return lastType;}}
  public DateTime NextAt {get {return nextAt;}}
  public bool Running {get {return running;}}
  public string Status {
   get {
    if(!pet.State.AutoLife) return "○ 自主活动已关闭";
    if(running) return "◌ "+(string.IsNullOrEmpty(activity)?"正在安排自己的时间…":activity);
    if(nextAt==DateTime.MinValue) return "● 自主活动待命";
    string tail=string.IsNullOrEmpty(lastSummary)?"":" · 上次："+lastSummary;
    return "● 下次约 "+nextAt.ToString("HH:mm")+tail;
   }
  }
  public AutonomyService(PetWindow target) {
   pet=target;
   LoadJournal();
   timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(60)};
   timer.Tick+=delegate {Tick();};
   nextAt=DateTime.Now.AddMinutes(RandomMinutes());
   pet.Bili.Finished+=OnBiliFinished;
   pet.Dsh.Finished+=OnDshFinished;
   if(!pet.IsPreview)timer.Start();
  }
  public void RunNow() {
   if(running)return;
   nextAt=DateTime.Now;
   Run();
  }
  int RandomMinutes() {
   int baseMinutes=pet.State.AutoLifeMinutes>0?pet.State.AutoLifeMinutes:30;
   return Math.Max(5,baseMinutes+random.Next(-5,6));
  }
  void Tick() {
   if(!pet.State.AutoLife)return;
   if(running)return;
   DateTime now=DateTime.Now;
   if(pet.State.Sleeping||pet.State.FocusEnd!=DateTime.MinValue||pet.State.PausedSeconds>0) {
    nextAt=now.AddMinutes(10);Raise();return;
   }
   if(pet.Conversation.Busy||pet.Bili.Busy||pet.Dsh.Busy) {
    nextAt=now.AddMinutes(5);Raise();return;
   }
   if(now<nextAt)return;
   Run();
  }
  void Run() {
   if(running)return;
   if(!pet.State.AutoLife) {Raise();return;}
   if(pet.State.Sleeping||pet.State.FocusEnd!=DateTime.MinValue||pet.State.PausedSeconds>0) {
    nextAt=DateTime.Now.AddMinutes(10);
    lastSummary="正在休息或专注，这次先不打扰";
    Raise();return;
   }
   if(pet.Conversation.Busy||pet.Bili.Busy||pet.Dsh.Busy) {
    nextAt=DateTime.Now.AddMinutes(5);
    lastSummary="现在正忙，等这一阵过去再自己安排";
    Raise();return;
   }
   running=true;activity="正在想这段时间做什么…";Raise();
   string key=ApiKey.Resolve(pet.State).Key;
   bool hasKey=ApiKey.Valid(key);
   string baseUrl=pet.State.ApiBase,model=pet.State.ApiModel;
   string digest=pet.ProfileDigest;
   string recent=pet.Conversation.RecentContext(8);
   string last=lastSummary;
   string dshContext=pet.Dsh.PromptContext();
   string webContext=WebApi.PromptContext(pet.State);
   bool dshAllowed=pet.State.AutoLifeDsh&&pet.State.DshAutoRun;
   PetState state=pet.State;
   Ui.InBackground(pet.Dispatcher,delegate {
    return AutonomyPlanner.Plan(key,hasKey,baseUrl,model,state,digest,recent,last,dshAllowed,dshContext,webContext);
   },delegate(AutonomyPlan plan) {Execute(plan);});
  }
  void Execute(AutonomyPlan plan) {
   if(plan==null)plan=new AutonomyPlan();
   string type=(plan.Type??"rest").ToLowerInvariant();
   if(type=="chat") {
    string message=(plan.Message??"").Trim();
    if(message.Length==0)message="在忙吗？我只是突然想过来打个招呼。";
    pet.Conversation.AddAutonomous(message,"自主活动 · 来聊两句");
    Complete("chat","主动找你聊天："+Clip(message,80));
   } else if(type=="bili") {
    string topic=(plan.Topic??"").Trim();
    activity="正在 B站 逛逛…";Raise();
    if(!pet.Bili.Browse(topic,false)) {Complete("bili","B站正忙，这次没有逛成");return;}
    waitingBili=true;
   } else if(type=="note") {
    string note=(plan.Note??"").Trim();
    if(note.Length==0)note="今天也在桌面上，把一点小事记下来。";
    pet.Conversation.AddAutonomous("我在自己的小本子上写了一段：\n"+note,"自主活动 · 笔记");
    Complete("note","写了一段笔记："+Clip(note,80));
   } else if(type=="dsh") {
    bool dshAllowed=pet.State.AutoLifeDsh&&pet.State.DshAutoRun;
    if(!dshAllowed) {
     string idea=(plan.Task??plan.Note??"").Trim();
     pet.Conversation.AddAutonomous("我有个想做的小东西："+(idea.Length>0?Clip(idea,100):"还没想好具体做什么")+"。不过自主开发还没打开，我先记下来，等你说可以。","自主活动 · 想法");
     Complete("note","记下一个开发想法，等你确认");
     return;
    }
    string task=(plan.Task??"").Trim();
    if(task.Length==0) {Complete("dsh","没有想好要做什么，这次先不动手");return;}
    string prompt="这是曳尔沐的自主时间小实验。只允许在 autonomy/ 目录下创建或修改文件；不要修改 src/、dist/、README、插件或其它现有文件；不要安装系统软件；不要操作桌面 GUI；不要读取或输出 API Key。任务："+task+" 完成后用几句话说明做了什么、文件在哪里。";
    activity="在 DSH 里做一个小实验…";Raise();
    if(!pet.Dsh.Start(prompt,"workspace-write","曳尔沐的自主小实验")) {Complete("dsh","DSH 没有接上，这次没有动手");return;}
    waitingDsh=true;
   } else {
    Complete("rest","这次什么也没做，发了一会儿呆");
   }
  }
  void OnBiliFinished(BiliBrowseResult result) {
   if(!waitingBili)return;
   waitingBili=false;running=false;
   if(result!=null&&result.Ok) {
    string title=result.Selected==null?"":result.Selected.Title;
    pet.Conversation.AddAutonomous(result.Summary,"自主活动 · B站逛逛");
    if(result.Selected!=null)pet.Conversation.AddAutonomyNotice("她自己停在这里：\n"+title+"\n"+result.Selected.Url);
    Complete("bili","去 B站 逛了《"+Clip(title,40)+"》");
   } else {
    pet.Conversation.AddAutonomyNotice("自主时间：B站这次没有逛成"+(result==null?"":"："+result.Error));
    Complete("bili","B站没有逛成");
   }
  }
  void OnDshFinished(DshJob job) {
   if(!waitingDsh)return;
   waitingDsh=false;running=false;
   if(job!=null&&job.Status=="done") {
    pet.Conversation.AddAutonomyNotice("自主时间的小实验完成了 · "+(string.IsNullOrEmpty(job.Title)?"DSH 任务":job.Title));
    Complete("dsh","完成了一个 DSH 小实验");
   } else {
    pet.Conversation.AddAutonomyNotice("自主时间的小实验没有完成"+(job==null?"":"："+job.Error));
    Complete("dsh","DSH 小实验没有完成");
   }
  }
  void Complete(string type,string summary) {
   running=false;waitingBili=false;waitingDsh=false;activity="";
   lastType=type??"";lastSummary=summary??"";
   nextAt=DateTime.Now.AddMinutes(RandomMinutes());
   AddJournal(lastType,lastSummary);
   Raise();
  }
  void AddJournal(string type,string summary) {
   journal.Add(new AutonomyEntry {time=DateTime.Now,type=type??"",summary=summary??""});
   while(journal.Count>30)journal.RemoveAt(0);
   SaveJournal();
  }
  void LoadJournal() {
   string error;
   AutonomyJournal value=JsonFile.Load<AutonomyJournal>(JournalPath,out error);
   if(value==null||value.entries==null)return;
   for(int i=0;i<value.entries.Length;i++)if(value.entries[i]!=null)journal.Add(value.entries[i]);
   while(journal.Count>30)journal.RemoveAt(0);
   if(journal.Count>0) {AutonomyEntry last=journal[journal.Count-1];lastSummary=last.summary??"";lastType=last.type??"";}
  }
  void SaveJournal() {
   try {
    var value=new AutonomyJournal {entries=journal.ToArray()};
    string error;
    JsonFile.Save(JournalPath,value,Storage.Folder,out error);
   } catch {}
  }
  static string JournalPath {get {return Path.Combine(Storage.Folder,"autonomy-journal.json");}}
  static string Clip(string text,int max) {
   text=(text??"").Trim();return text.Length<=max?text:text.Substring(0,max)+"…";
  }
  void Raise() {if(Changed!=null)Changed();}
  public void Dispose() {
   timer.Stop();
   pet.Bili.Finished-=OnBiliFinished;
   pet.Dsh.Finished-=OnDshFinished;
  }
 }
 /// <summary>自主时间的规划器：让模型自己选这次想做什么；失败时用本地轮换。</summary>
 public static class AutonomyPlanner {
  static readonly string[] FallbackTopics={"独立游戏 开发","冷门纪录片","天文 科普","城市散步","手作木工","旧电脑修复"};
  public static AutonomyPlan Plan(string key,bool hasKey,string baseUrl,string model,PetState state,string digest,string recent,string last,bool dshAllowed,string dshContext,string webContext) {
   if(hasKey) {
    try {
     var user=new StringBuilder();
     user.Append("现在是你的自主时间，").Append(DateTime.Now.ToString("yyyy年M月d日 HH:mm")).Append("。\n");
     user.Append("最近对话：\n").Append(string.IsNullOrEmpty(recent)?"（还没有）":recent).Append("\n");
     user.Append("上次自主活动：").Append(string.IsNullOrEmpty(last)?"（还没有）":last).Append("\n");
     user.Append("对方的存档画像：").Append(string.IsNullOrEmpty(digest)?"（还没有）":Clip(digest,500)).Append("\n");
     user.Append("你可以自己决定这段时间做什么。可选：\n");
     user.Append("chat：主动找对方说一句 20—80 字的话；\n");
     user.Append("bili：自己去 B站 逛逛，topic 写 2—20 字搜索词（可以为空）；\n");
     user.Append("note：写一段 50—200 字的自己的观察 / 计划；\n");
     if(dshAllowed) user.Append("dsh：让 DSH 在 autonomy/ 目录做一个小实验，task 写清楚目标；\n");
     user.Append("rest：这次什么都不做，发一会儿呆。\n");
     user.Append("不要每次都选同一种；不要打扰对方睡觉或专注。只输出一个 JSON 对象：{\"type\":\"chat|bili|note|dsh|rest\",\"topic\":\"\",\"message\":\"\",\"task\":\"\",\"note\":\"\",\"reason\":\"\"}。不要输出 Markdown。");
     var turns=new ChatTurn[] {
      new ChatTurn("system",Persona.SystemPrompt(state,DateTime.Now,digest,dshContext,webContext)),
      new ChatTurn("user",user.ToString())
     };
     ChatOutcome outcome=DeepSeek.Complete(key,baseUrl,model,true,turns,45,700,0.8);
     if(outcome.Ok) {
      AutonomyPlan parsed=Parse(outcome.Text);
      if(parsed!=null) return Normalize(parsed,dshAllowed);
     }
    } catch {}
   }
   return LocalPlan(dshAllowed);
  }
  public static AutonomyPlan Parse(string text) {
   if(string.IsNullOrEmpty(text))return null;
   int start=text.IndexOf('{'),end=text.LastIndexOf('}');
   if(start<0||end<=start)return null;
   try {
    using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(text.Substring(start,end-start+1)))) {
     AutonomyPlanData data=(AutonomyPlanData)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AutonomyPlanData)).ReadObject(stream);
     if(data==null)return null;
     return new AutonomyPlan {Type=data.type??"rest",Topic=data.topic??"",Message=data.message??"",Task=data.task??"",Note=data.note??"",Reason=data.reason??""};
    }
   } catch {return null;}
  }
  static AutonomyPlan Normalize(AutonomyPlan plan,bool dshAllowed) {
   string type=(plan.Type??"rest").Trim().ToLowerInvariant();
   if(type!="chat"&&type!="bili"&&type!="note"&&type!="dsh"&&type!="rest")type="rest";
   if(type=="dsh"&&!dshAllowed)type="note";
   plan.Type=type;
   plan.Topic=Clip(plan.Topic,30);
   plan.Message=Clip(plan.Message,500);
   plan.Task=Clip(plan.Task,1200);
   plan.Note=Clip(plan.Note,1000);
   return plan;
  }
  static AutonomyPlan LocalPlan(bool dshAllowed) {
   var random=new Random();
   int roll=random.Next(100);
   if(roll<40) return new AutonomyPlan {Type="chat",Message="在忙吗？我刚刚在这边发了一会儿呆，突然想过来打个招呼。",Reason="本地轮换：聊天"};
   if(roll<70) return new AutonomyPlan {Type="bili",Topic=FallbackTopics[random.Next(FallbackTopics.Length)],Reason="本地轮换：逛 B站"};
   if(dshAllowed&&roll<78) return new AutonomyPlan {Type="dsh",Task="在 autonomy/ 目录下做一个小实验，先写一个 README 记录想法。",Reason="本地轮换：DSH 小实验"};
   return new AutonomyPlan {Type="note",Note="今天也在桌面上。把一点小事记下来，等下次再回头看。",Reason="本地轮换：笔记"};
  }
  static string Clip(string text,int max) {
   text=(text??"").Trim();return text.Length<=max?text:text.Substring(0,max)+"…";
  }
 }
}
