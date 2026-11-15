using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
namespace ElymsPet {
 [DataContract] public sealed class RepositoryExplanationCall {
  [DataMember] public string Stage,Effort,FinishReason,Error;
  [DataMember] public int Attempt,TokenBudget,PromptTokens,CompletionTokens,ReasoningTokens,Characters;
  [DataMember] public bool Ok,Truncated,ResponseClipped;
  [DataMember] public double Seconds;
 }
 public static class RepositoryExplanationCompletion {
  public const string Revision="reader-low-retry-v2";
  static string Stage(ChatTurn[] turns,bool reader){
   foreach(var t in turns)if(t.Role=="system"){
    if(t.Content==RepositoryExplanationCodeReview.Rules)return "code.reader";
    if(t.Content.Contains(RepositoryExplanationReview.ObjectReaderRules))return "objects.reader";
    if(t.Content.Contains(RepositoryExplanationReview.FlowReaderRules))return "flow.reader";
    if(t.Content.Contains(RepositoryExplanationReview.RevisionRules))return "objects.author";
    if(t.Content.Contains(RepositoryExplanationReview.FlowAnswerRules))return "flow.author";
   }
   return reader?"reader":"draft.author";
  }
  public static ChatOutcome Complete(IArchiveApi api,string key,string url,string model,ChatTurn[] turns,bool reader,RepositoryExplanationLog log,Func<bool> cancelled=null){
   string stage=Stage(turns,reader);var input=new List<ChatTurn>(turns);int budget=reader?32768:8192;
   double seconds=0;int tokens=0;ChatOutcome result=null;
   for(int attempt=1;attempt<=2;attempt++){
    if(NetworkCancellation.IsCancellationRequested||(cancelled!=null&&cancelled()))throw new OperationCanceledException("讲解已取消。");
    var specialized=api as IRepositoryExplanationApi;
    result=specialized!=null?specialized.CompleteExplanation(key,url,model,input.ToArray(),reader,budget):api.Complete(key,url,model,true,input.ToArray(),180,budget,0.1);
    if(log.Calls==null)log.Calls=new List<RepositoryExplanationCall>();
    log.Calls.Add(new RepositoryExplanationCall{Stage=stage,Attempt=attempt,TokenBudget=budget,Effort=specialized!=null?(reader?"low":"high"):"provider-default",Ok=result!=null&&result.Ok,Truncated=result!=null&&result.Truncated,ResponseClipped=result!=null&&result.ResponseClipped,FinishReason=result==null?"":result.FinishReason,PromptTokens=result==null?0:result.PromptTokens,CompletionTokens=result==null?0:result.CompletionTokens,ReasoningTokens=result==null?0:result.ReasoningTokens,Characters=result==null?0:result.ResponseChars>0?result.ResponseChars:(result.Text??"").Length,Seconds=result==null?0:result.Seconds,Error=result==null?"没有响应":result.Error});
    if(result!=null){seconds+=result.Seconds;tokens+=result.CompletionTokens;}
    if(NetworkCancellation.IsCancellationRequested||(cancelled!=null&&cancelled()))throw new OperationCanceledException("讲解已取消。");
    if(result==null||(!result.Truncated&&!result.ResponseClipped&&result.FinishReason!="length")||attempt==2)break;
    budget*=2;
    input.Add(new ChatTurn("system","上次本阶段输出达到长度限制。请在本次请求中直接给出完整结果，保持本阶段要求的输出格式；合并重复说明，不输出思考过程，不复述任务要求。不要重查源码或请求工具。"));
   }
   if(result!=null){result.Seconds=seconds;result.CompletionTokens=tokens;}return result;
  }
 }
}
