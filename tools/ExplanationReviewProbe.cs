using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using ElymsPet;
public static class ExplanationReviewProbe {
 public static int Main(string[] args){try{
  string source=Path.GetFullPath(args[0]),folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);
  var prior=GraphEditing.Parse<RepositoryExplanationLog>(File.ReadAllText(Path.Combine(source,"result.json")));
  // Import the existing probe's saved, role-delimited final author request.
  string raw=File.ReadAllText(Path.Combine(source,"call-2-input.txt"));var matches=Regex.Matches(raw,@"(?m)^ROLE: (system|user|assistant)\r?$");var context=new List<ChatTurn>();
  for(int i=0;i<matches.Count;i++){int start=matches[i].Index+matches[i].Length,end=i+1<matches.Count?matches[i+1].Index:raw.Length;context.Add(new ChatTurn(matches[i].Groups[1].Value,raw.Substring(start,end-start).Trim()));}
  if(context.Count<4||!raw.Contains("DSH 只读源码任务已完成"))throw new Exception("Missing original author evidence");
  var state=Storage.Load();var key=ApiKey.Resolve(state);var api=new ArchiveApi();
  var log=new RepositoryExplanationLog{SlotId=prior.SlotId,TopicId=prior.TopicId,NodeId=prior.NodeId,NodeTitle=prior.NodeTitle,Focus=prior.Focus,Model=state.ApiModel,Prompt=prior.Prompt,SourceRevision=prior.SourceRevision,PromptRevision=RepositoryExplanationStore.PromptRevision,ResumeFromRequestId=prior.RequestId};
  File.WriteAllText(Path.Combine(folder,"initial.txt"),prior.Output,new UTF8Encoding(false));
  int round=0;
  var result=RepositoryExplanationReview.Review(new ChatOutcome{Ok=true,Text=prior.Output},context.ToArray(),turns=>{
   round++;
   File.WriteAllText(Path.Combine(folder,"round-"+round+"-reader-input.txt"),turns[0].Content+"\n\n"+turns[1].Content,new UTF8Encoding(false));
   var response=round==1&&args.Length>2?new ChatOutcome{Ok=true,Text=File.ReadAllText(args[2])}:RepositoryExplanationCompletion.Complete(api,key.Key,state.ApiBase,state.ApiModel,turns,true,log);
   File.WriteAllText(Path.Combine(folder,"round-"+round+"-reader-output.json"),response.Text??"",new UTF8Encoding(false));return response;
  },turns=>{
   Console.WriteLine("Reader questions saved. Waiting for local reviewer approval.");
   string decision=Path.Combine(folder,"round-"+round+"-review-decision.txt");var until=DateTime.UtcNow.AddMinutes(5);
   while(!File.Exists(decision)){if(DateTime.UtcNow>until)throw new InvalidOperationException("Reader questions not approved before timeout");Thread.Sleep(250);}
   if(File.ReadAllText(decision).Trim()!="approve")throw new InvalidOperationException("Reader questions rejected; revise reader prompt first");
   var text=new StringBuilder();foreach(var turn in turns)text.Append("ROLE: ").Append(turn.Role).Append('\n').Append(turn.Content).Append("\n\n");
   File.WriteAllText(Path.Combine(folder,"round-"+round+"-author-input.txt"),text.ToString(),new UTF8Encoding(false));
   var response=RepositoryExplanationCompletion.Complete(api,key.Key,state.ApiBase,state.ApiModel,turns,false,log);
   File.WriteAllText(Path.Combine(folder,"round-"+round+"-author-output.txt"),response.Text??"",new UTF8Encoding(false));return response;
  },log,null,Console.WriteLine);
  log.Save(folder,result,key.Key);
  File.WriteAllText(Path.Combine(folder,"result.json"),GraphEditing.Json(log),new UTF8Encoding(false));File.WriteAllText(Path.Combine(folder,"output.txt"),log.DisplayText,new UTF8Encoding(false));
  if(Array.IndexOf(args,"--publish")>=0&&RepositoryExplanationStore.Reusable(log)){
   if(!log.Save(Storage.Folder,result,key.Key))throw new Exception("Could not save production explanation log");
   string error;if(!new RepositoryExplanationStore(Storage.Folder).Save(log,key.Key,out error))throw new Exception("Could not save explanation: "+error);
  }
  Console.WriteLine("Review status: "+log.ReviewStatus);return log.ReviewStatus=="complete"||log.ReviewStatus=="no_questions"?0:1;
 }catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);return 1;}}
}
