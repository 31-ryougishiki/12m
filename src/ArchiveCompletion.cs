using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace ElymsPet {
 // Retry only the failed model stage, never replay web tools or merge partial results.
 public static class ArchiveCompletion {
  public static ChatOutcome Complete(IArchiveApi api,string key,string url,string model,ChatTurn[] turns,int tokens,double temperature,bool extraction) {
   var input=new List<ChatTurn>(turns);
   ChatOutcome outcome=null;
   for(int attempt=0;attempt<2;attempt++) {
    try {outcome=api.Complete(key,url,model,true,input.ToArray(),150,tokens,temperature);}
    catch(WebException) {outcome=ChatOutcome.Fail("网络请求中断。","network",0,0);}
    catch(IOException) {outcome=ChatOutcome.Fail("网络读取中断。","read",0,0);}
    if(Valid(outcome,extraction))return outcome;
    if(outcome!=null&&!outcome.Ok&&outcome.Status!=0&&outcome.Status!=200&&outcome.Status!=408&&outcome.Status!=429&&outcome.Status<500)return outcome;
    if(attempt==0)input.Add(new ChatTurn("user",extraction?"上次调用未得到完整有效的 JSON。请基于相同原话重新整理，只输出完整 JSON；不要编造内容。":"上次调用未得到完整有效的评估和问题。请基于相同上下文只输出含 reply 与完整 assessment 的 JSON，判断能否协助任务并追问关键缺口或提议最小试做。"));
   }
   if(outcome!=null&&!outcome.Ok)return outcome;
   return ChatOutcome.Fail(outcome!=null&&outcome.Truncated?"输出未完整结束，自动重试一次仍未成功。":"未收到有效的"+(extraction?"整理结果":"问题")+"，自动重试一次仍未成功。","invalid archive response",200,0);
  }
  public static bool Valid(ChatOutcome outcome,bool extraction) {
   if(outcome==null||!outcome.Ok||outcome.Truncated||string.IsNullOrWhiteSpace(outcome.Text))return false;
   if(extraction)return ArchiveInterview.Parse(outcome.Text).Structured;
   return ArchiveAssessment.Parse(outcome.Text)!=null&&ArchiveInterview.LooksLikeQuestion(ArchiveInterview.CleanQuestion(outcome.Text));
  }
 }
}
