using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using ElymsPet;
class ExplanationCodeProbe {
 static int Main(string[] args){try{
  string source=Path.GetFullPath(args[0]),folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);
  var log=GraphEditing.Parse<RepositoryExplanationLog>(File.ReadAllText(Path.Combine(source,"result.json")));
  log.ResumeFromRequestId=log.RequestId;log.RequestId=Guid.NewGuid().ToString("N");log.StartedAt=DateTimeOffset.Now.ToString("o");log.PromptRevision=RepositoryExplanationStore.PromptRevision;
  ChatOutcome result;
  if(log.ReviewStatus=="failed"&&log.ObjectReviewStatus=="complete"&&File.Exists(Path.Combine(source,"round-2-author-output.txt"))){
   // Revalidate the real saved model output after a parser-only compatibility fix.
   string body=File.ReadAllText(Path.Combine(source,"round-1-author-output.txt"));int round=0;var repaired=new RepositoryExplanationLog();
   result=RepositoryExplanationReview.Review(new ChatOutcome{Ok=true,Text=body},new ChatTurn[0],t=>new ChatOutcome{Ok=true,Text=++round==1?"{\"Questions\":[]}":File.ReadAllText(Path.Combine(source,"round-2-reader-output.json"))},t=>new ChatOutcome{Ok=true,Text=File.ReadAllText(Path.Combine(source,"round-2-author-output.txt"))},repaired);
   if(repaired.ReviewStatus!="complete")throw new Exception("Saved flow answer still invalid: "+repaired.ReviewError);
   log.ObjectReviewedOutput=body;log.FlowQuestions=repaired.FlowQuestions;log.ReaderQuestions=repaired.ReaderQuestions;log.FlowAnswers=repaired.FlowAnswers;log.FlowAppendix=repaired.FlowAppendix;log.FlowReviewStatus=repaired.FlowReviewStatus;log.ReviewStatus="complete";log.ReviewError="";
  }else{if(!RepositoryExplanationStore.Reusable(log))throw new Exception("Input explanation is not complete");result=new ChatOutcome{Ok=true,Text=log.Output};}
  result.Seconds=log.Seconds;log.CodeSourceWarnings=new List<string>();
  var graph=GraphEditing.Parse<DomainGraph>(File.ReadAllText(Path.Combine(source,"graph.json")));
  var node=graph.Nodes.Find(n=>n.Id==log.NodeId);var settings=LocalRepository.Load();
  string evidence=File.Exists(Path.Combine(source,"round-1-author-input.txt"))?File.ReadAllText(Path.Combine(source,"round-1-author-input.txt")):"";
  var sources=RepositoryExplanationCodeReview.Sources(result.Text,RepositoryLearning.CodeLocations(node),evidence,settings,log.CodeSourceWarnings);
  Console.WriteLine("Verified source files: "+sources.Count+"; sentences: "+RepositoryExplanationCodeReview.Sentences(result.Text).Count);
  File.WriteAllText(Path.Combine(folder,"before-code-review.txt"),result.Text,new UTF8Encoding(false));
  var state=Storage.Load();var key=ApiKey.Resolve(state);var api=new ArchiveApi();
  result=RepositoryExplanationCodeReview.Run(result,sources,turns=>{
   File.WriteAllText(Path.Combine(folder,"code-reader-input.txt"),turns[0].Content+"\n\n"+turns[1].Content,new UTF8Encoding(false));
   string replay=Array.Find(args,a=>a.StartsWith("--replay-code=",StringComparison.Ordinal));ChatOutcome response;
   if(replay!=null){
    string replayFolder=Path.GetFullPath(replay.Substring("--replay-code=".Length));
    if(File.ReadAllText(Path.Combine(replayFolder,"code-reader-input.txt"))!=turns[0].Content+"\n\n"+turns[1].Content)throw new Exception("Replay evidence or prompt changed");
    var original=GraphEditing.Parse<RepositoryExplanationLog>(File.ReadAllText(Path.Combine(replayFolder,"result.json")));log.ResumeFromRequestId=original.RequestId;
    if(log.Calls==null)log.Calls=new List<RepositoryExplanationCall>();foreach(var call in original.Calls)if(call.Stage=="code.reader")log.Calls.Add(call);
    response=new ChatOutcome{Ok=true,Text=File.ReadAllText(Path.Combine(replayFolder,"code-reader-output.json"))};
   }else response=RepositoryExplanationCompletion.Complete(api,key.Key,state.ApiBase,state.ApiModel,turns,true,log);
   File.WriteAllText(Path.Combine(folder,"code-reader-output.json"),response.Text??"",new UTF8Encoding(false));return response;
  },log,null,Console.WriteLine);
  log.Save(folder,result,key.Key);File.WriteAllText(Path.Combine(folder,"result.json"),GraphEditing.Json(log),new UTF8Encoding(false));File.WriteAllText(Path.Combine(folder,"output.txt"),log.DisplayText,new UTF8Encoding(false));
  Console.WriteLine("Code review: "+log.CodeReviewStatus+"; anchors: "+(log.CodeAnchors==null?0:log.CodeAnchors.Count)+"; error: "+log.CodeReviewError);
  if(Array.IndexOf(args,"--publish")>=0&&RepositoryExplanationStore.Reusable(log)){
   Console.WriteLine("Awaiting code mapping audit before publishing.");string decision=Path.Combine(folder,"code-review-decision.txt");var until=DateTime.UtcNow.AddMinutes(10);
   while(!File.Exists(decision)){if(DateTime.UtcNow>until)throw new Exception("Code mapping audit timed out");Thread.Sleep(250);}
   if(File.ReadAllText(decision).Trim()!="approve")throw new Exception("Code mapping rejected for prompt revision");
   if(!log.Save(Storage.Folder,result,key.Key))throw new Exception("Could not save production log");string error;if(!new RepositoryExplanationStore(Storage.Folder).Save(log,key.Key,out error))throw new Exception(error);
  }
  return RepositoryExplanationStore.Reusable(log)?0:1;
 }catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);return 1;}}
}
