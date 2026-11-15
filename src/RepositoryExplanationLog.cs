using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
namespace ElymsPet {
 [DataContract] public sealed class RepositoryExplanationLog {
  static readonly object gate=new object();
  [DataMember] public string RequestId=Guid.NewGuid().ToString("N"),StartedAt=DateTimeOffset.Now.ToString("o"),CompletedAt;
  [DataMember] public string SlotId,TopicId,NodeId,NodeTitle,Focus,Model,Prompt,Output,DisplayText,Error,FinishReason;
  [DataMember] public bool Ok,Truncated,ResponseClipped;
  [DataMember] public string SourceRevision,PromptRevision;
  [DataMember] public string ResumeFromRequestId;
  [DataMember] public double Seconds;
  [DataMember] public System.Collections.Generic.List<RepositoryExplanationCall> Calls;
  [DataMember] public string CodeReviewStatus,CodeReviewError;
  [DataMember] public System.Collections.Generic.List<ExplanationCodeAnchor> CodeAnchors;
  [DataMember] public System.Collections.Generic.List<ExplanationCodeGap> CodeUnmapped;
  [DataMember] public System.Collections.Generic.List<string> CodeSourceWarnings;
  [DataMember] public string InitialOutput,ReviewStatus,ReviewError;
  [DataMember] public System.Collections.Generic.List<RepositoryExplanationReview.ReaderQuestion> ReaderQuestions;
  [DataMember] public string ObjectReviewStatus,FlowReviewStatus,ObjectReviewedOutput,FlowAppendix;
  [DataMember] public System.Collections.Generic.List<RepositoryExplanationReview.ReaderQuestion> ObjectQuestions,FlowQuestions;
  [DataMember] public System.Collections.Generic.List<RepositoryExplanationReview.FlowAnswer> FlowAnswers;
  public static string Format(ChatOutcome result){
   return result==null||!result.Ok||string.IsNullOrWhiteSpace(result.Text)?(result!=null&&!string.IsNullOrWhiteSpace(result.Error)?result.Error:"讲解失败，请稍后重试。"):(result.Truncated?"回答不完整，请缩小问题后重试。\n":"AI 讲解 · 待核实\n")+result.Text;
  }
  public bool Save(string root,ChatOutcome result,string apiKey){
   CompletedAt=DateTimeOffset.Now.ToString("o");Ok=result!=null&&result.Ok;Truncated=result!=null&&result.Truncated;ResponseClipped=result!=null&&result.ResponseClipped;
   Output=result==null?"":result.Text;Error=result==null?"未收到结果":result.Error;DisplayText=Format(result);
   if(ReviewStatus=="failed")DisplayText+=FlowReviewStatus=="failed"?"\n（第二轮流程答疑未完成，已保留第一轮完成的讲解正文。）":"\n（第一轮代码对应审阅未完成，当前显示初稿。）";
   if(CodeReviewStatus=="failed")DisplayText+="\n（第三轮源码定位未完成，当前保留讲解正文。）";
   FinishReason=result==null?"":result.FinishReason;Seconds=result==null?0:result.Seconds;
   try{
    string json=GraphEditing.Json(this);
    if(!string.IsNullOrEmpty(apiKey))json=json.Replace(apiKey,"[REDACTED]");
    lock(gate){string folder=Path.Combine(root,"repository-explanations");Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd")+".jsonl"),json+Environment.NewLine,new UTF8Encoding(false));}
    return true;
   }catch{return false;}
  }
 }
}
