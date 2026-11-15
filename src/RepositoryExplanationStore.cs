using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
namespace ElymsPet {
 [DataContract] public sealed class SavedRepositoryExplanation {
  [DataMember] public int Schema=1;
  [DataMember] public RepositoryExplanationLog Result;
 }
 // Durable reading results are separate from the graph and append-only diagnostics.
 // A null root is an isolated, memory-only store for previews and tests.
 public sealed class RepositoryExplanationStore {
  readonly string root;readonly object gate=new object();
  readonly Dictionary<string,RepositoryExplanationLog> memory=new Dictionary<string,RepositoryExplanationLog>();
  Dictionary<string,RepositoryExplanationLog> history;string historyError="";
  public RepositoryExplanationStore(string root){this.root=root==null?null:Path.GetFullPath(root);}
  static string Hash(string text){return ArchiveCloudCodec.Hash(Encoding.UTF8.GetBytes(text??""));}
  static string Key(string slot,string topic,string node){return Hash(GraphEditing.Json(new[]{slot,topic,node}));}
  public static string PromptRevision {get{return Hash(RepositoryExplanationCompletion.Revision+RepositoryExplanationCodeReview.Rules+RepositoryExplanationPrompt.Rules+RepositoryExplanationReview.ReaderRules+RepositoryExplanationReview.ObjectReaderRules+RepositoryExplanationReview.FlowReaderRules+RepositoryExplanationReview.ReaderScopeRules+RepositoryExplanationReview.RevisionRules+RepositoryExplanationReview.FlowAnswerRules+RepositoryExplanationReview.EvidenceRules);}}
  public string PathFor(string slot,string topic,string node){return root==null?null:Path.Combine(root,"learning","explanations",Hash(slot).Substring(0,24),Hash(topic).Substring(0,24),Hash(node).Substring(0,24),"latest.json");}
  public static bool Reusable(RepositoryExplanationLog value){return value!=null&&value.Ok&&!value.Truncated&&!value.ResponseClipped&&value.FinishReason!="length"&&value.CodeReviewStatus!="failed"&&value.CodeReviewStatus!="cancelled"&&!string.IsNullOrWhiteSpace(value.Output)&&value.Focus==RepositoryLearning.ExplanationFocus&&!string.IsNullOrWhiteSpace(value.SlotId)&&!string.IsNullOrWhiteSpace(value.TopicId)&&!string.IsNullOrWhiteSpace(value.NodeId)&&(string.IsNullOrEmpty(value.ReviewStatus)||value.ReviewStatus=="complete"||value.ReviewStatus=="no_questions");}
  static DateTimeOffset Time(RepositoryExplanationLog value){DateTimeOffset time;return value!=null&&(DateTimeOffset.TryParse(value.CompletedAt,out time)||DateTimeOffset.TryParse(value.StartedAt,out time))?time:DateTimeOffset.MinValue;}
  static bool Matches(RepositoryExplanationLog value,string slot,string topic,string node){return Reusable(value)&&value.SlotId==slot&&value.TopicId==topic&&value.NodeId==node;}
  static RepositoryExplanationLog Copy(RepositoryExplanationLog value,string secret){string json=GraphEditing.Json(value);return GraphEditing.Parse<RepositoryExplanationLog>(string.IsNullOrEmpty(secret)?json:json.Replace(secret,"[REDACTED]"));}
  void ReadHistory(){
   if(history!=null)return;history=new Dictionary<string,RepositoryExplanationLog>();if(root==null)return;
   string folder=Path.Combine(root,"repository-explanations");if(!Directory.Exists(folder))return;
   try{foreach(string path in Directory.GetFiles(folder,"*.jsonl")){
    try{using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream,Encoding.UTF8)){
     string line;while((line=reader.ReadLine())!=null){
      RepositoryExplanationLog item;try{item=GraphEditing.Parse<RepositoryExplanationLog>(line);}catch{continue;}
      if(!Reusable(item))continue;string key=Key(item.SlotId,item.TopicId,item.NodeId);RepositoryExplanationLog previous;
      if(!history.TryGetValue(key,out previous)||Time(item)>Time(previous))history[key]=item;
     }
    }}catch(IOException){historyError="部分历史讲解暂时无法读取。";}catch(UnauthorizedAccessException){historyError="部分历史讲解无读取权限。";}
   }}catch(IOException){historyError="历史讲解目录暂时无法读取。";}catch(UnauthorizedAccessException){historyError="历史讲解目录无读取权限。";}
  }
  public RepositoryExplanationLog Load(string slot,string topic,string node,out string warning){
   lock(gate){
    warning="";string key=Key(slot,topic,node);RepositoryExplanationLog current=null;
    if(root==null){memory.TryGetValue(key,out current);return current==null?null:Copy(current,null);}
    string error;var saved=JsonFile.Load<SavedRepositoryExplanation>(PathFor(slot,topic,node),out error);
    if(saved!=null&&saved.Schema==1&&Matches(saved.Result,slot,topic,node))current=saved.Result;
    if(saved!=null&&current==null)warning="已保存讲解格式或节点不匹配，将尝试从历史记录恢复。";
    if(!string.IsNullOrEmpty(error))warning="已保存讲解读取失败，将尝试从历史记录恢复。";
    ReadHistory();RepositoryExplanationLog legacy;
    if(history.TryGetValue(key,out legacy)&&(current==null||Time(legacy)>Time(current))){
     current=legacy;string saveError;
     if(!Save(current,null,out saveError))warning="已从历史记录读取讲解，但保存副本失败："+saveError;
    }
    if(historyError.Length>0)warning+=(warning.Length>0?"\n":"")+historyError;
    return current==null?null:Copy(current,null);
   }
  }
  public bool Save(RepositoryExplanationLog value,string apiKey,out string error){
   lock(gate){
    error="";if(!Reusable(value)){error="仅保存已完整生成并完成审阅的节点讲解。";return false;}
    try{
     var clean=Copy(value,apiKey);string key=Key(clean.SlotId,clean.TopicId,clean.NodeId);
     if(root==null){memory[key]=clean;return true;}
     string path=PathFor(clean.SlotId,clean.TopicId,clean.NodeId);
     if(!JsonFile.Save(path,new SavedRepositoryExplanation{Result=clean},Path.GetDirectoryName(path),out error))return false;
     if(history!=null)history[key]=clean;return true;
    }catch(Exception ex){error=ex.Message;return false;}
   }
  }
  public static string Notice(RepositoryExplanationLog value,string graphRevision){
   if(value==null)return "";DateTimeOffset time;string label="已保存的讲解";
   if(DateTimeOffset.TryParse(value.CompletedAt,out time))label+=" · "+time.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
   if(string.IsNullOrEmpty(value.SourceRevision))label+="\n这是历史讲解，生成时的资料版本未记录；可重新生成以核对当前内容。";
   else if(value.SourceRevision!=graphRevision)label+="\n图谱资料已变化，当前显示上次讲解，可重新生成。";
   if(value.PromptRevision!=PromptRevision)label+="\n讲解规则已更新，可重新生成以使用新规则。";
   return label;
  }
 }
}
