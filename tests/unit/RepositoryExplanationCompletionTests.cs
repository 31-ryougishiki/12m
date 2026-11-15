using System;
using System.Collections.Generic;
namespace ElymsPet {
 static class RepositoryExplanationCompletionTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS explanation completion: "+name);}
  sealed class Api : IArchiveApi,IRepositoryExplanationApi {
   public readonly Queue<ChatOutcome> Results=new Queue<ChatOutcome>();public readonly List<int> Budgets=new List<int>();public bool Reader;public ChatTurn[] Last;
   public ChatOutcome CompleteExplanation(string k,string u,string m,ChatTurn[] t,bool reader,int tokens){Reader=reader;Budgets.Add(tokens);Last=t;return Results.Dequeue();}
   public ChatOutcome Complete(string k,string u,string m,bool think,ChatTurn[] t,int timeout,int tokens,double temperature){throw new Exception("Specialized explanation API must be used");}
   public WebOutcome Search(string k,string u,string q){throw new NotImplementedException();}public WebOutcome Fetch(string k,string u,string q){throw new NotImplementedException();}
  }
  public static void Run(){
   const string raw="{\"model\":\"test\",\"choices\":[{\"message\":{\"content\":\"\",\"reasoning_content\":\"private chain\"},\"finish_reason\":\"length\"}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":16384,\"completion_tokens_details\":{\"reasoning_tokens\":16384}}}";
   var empty=DeepSeek.ParseResponse(raw,2);Check(!empty.Ok&&empty.Truncated&&empty.FinishReason=="length"&&empty.CompletionTokens==16384&&empty.ReasoningTokens==16384&&empty.HasReasoning,"empty-content truncation preserves finish reason and usage");
   var api=new Api();api.Results.Enqueue(empty);api.Results.Enqueue(new ChatOutcome{Ok=true,Text="{\"Questions\":[]}",FinishReason="stop",CompletionTokens=300,Seconds=1});var log=new RepositoryExplanationLog();
   var turns=new[]{new ChatTurn("system",RepositoryExplanationReview.ReaderRules+RepositoryExplanationReview.ObjectReaderRules),new ChatTurn("user","讲解正文")};
   var result=RepositoryExplanationCompletion.Complete(api,"secret","url","model",turns,true,log);
   Check(result.Ok&&api.Reader&&api.Budgets.Count==2&&api.Budgets[0]==32768&&api.Budgets[1]==65536,"reader uses specialized low-effort path and retries truncated request once with larger budget");
   Check(api.Last[1].Content==turns[1].Content&&log.Calls[0].Stage=="objects.reader"&&log.Calls[0].Effort=="low"&&log.Calls[0].ReasoningTokens==16384&&log.Calls[1].Attempt==2&&result.Seconds==3,"retry keeps same evidence and records per-call diagnostics");
   Check(!GraphEditing.Json(log.Calls).Contains("private chain")&&!GraphEditing.Json(log.Calls).Contains("secret"),"diagnostics exclude reasoning text and credentials");
   api=new Api();api.Results.Enqueue(empty);api.Results.Enqueue(empty);log=new RepositoryExplanationLog();result=RepositoryExplanationCompletion.Complete(api,"","","",turns,true,log);Check(!result.Ok&&api.Budgets.Count==2,"repeated truncation is bounded, never treated as success");
   api=new Api();api.Results.Enqueue(ChatOutcome.Fail("network","",500,0));log=new RepositoryExplanationLog();RepositoryExplanationCompletion.Complete(api,"","","",turns,true,log);Check(api.Budgets.Count==1,"non-truncation failures do not trigger blind retries");
   api=new Api();api.Results.Enqueue(new ChatOutcome{Ok=true,Text="正文"});log=new RepositoryExplanationLog();RepositoryExplanationCompletion.Complete(api,"","","",new[]{new ChatTurn("system",RepositoryExplanationReview.RevisionRules)},false,log);Check(!api.Reader&&api.Budgets[0]==8192&&log.Calls[0].Effort=="high"&&log.Calls[0].Stage=="objects.author","author retains high effort separately from reader");
   api=new Api();bool stopped=false;try{RepositoryExplanationCompletion.Complete(api,"","","",turns,true,new RepositoryExplanationLog(),()=>true);}catch(OperationCanceledException){stopped=true;}Check(stopped&&api.Budgets.Count==0,"cancellation prevents model calls");
  }
 }
}
