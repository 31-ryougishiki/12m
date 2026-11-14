using System;
using System.Collections.Generic;
using System.IO;
namespace ElymsPet {
 static class DshSourceResearchTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("DSH source: "+name);Console.WriteLine("PASS DSH source: "+name);}
  sealed class Client : IDshSourceClient {
   public int Starts,Polls,Cancels;public string Task;public DshJob Result=new DshJob{Id="read-job",Status="done",Tools=4,Text="LoggingStatLogger.record reads iteration_details (loggers.py:187)."};
   public DshJob Start(string task){Task=task;Starts++;return new DshJob{Id="read-job",Status="queued"};}
   public DshJob Poll(string id){Check(id=="read-job","poll uses the submitted job id");Polls++;return Result;}
   public bool Cancel(string id){Cancels++;return true;}
  }
  public static void Run(string root){
   string previous=Storage.Folder,repo=Path.Combine(root,"DSH 源码");Directory.CreateDirectory(repo);
   try{
    Storage.Folder=Path.Combine(root,"DSH-source-state");string commit=new string('a',40),url="https://github.com/org/repo/blob/"+commit+"/engine.py#L1";
    var settings=new LocalRepositorySettings{Repositories=new List<LocalRepositoryMapping>{new LocalRepositoryMapping{Repository="org/repo",Folder=repo}}};
    var turns=new[]{new ChatTurn("system","rules"),new ChatTurn("user","step 里的 iteration_details"),new ChatTurn("assistant","写进输出包。"),new ChatTurn("user","后面在哪里使用？")};
    Func<Client,DshSourceResearch> make=c=>new DshSourceResearch(settings,new[]{url},"graph / engine-step",c,()=>{},2);
    var client=new Client();int calls=0;var messages=new List<string>();
    var result=make(client).Complete(turns,input=>{
     calls++;if(calls==1){Check(input[input.Length-1].Content.Contains("[[DSH_READ]]")&&!input[input.Length-1].Content.Contains("\"Queries\""),"model requests plain-text DSH research, not source JSON");return new ChatOutcome{Ok=true,Text="[[DSH_READ]]追踪 iteration_details 的使用点[[/DSH_READ]]"};}
     Check(client.Polls==1&&input[input.Length-1].Content.Contains("loggers.py:187")&&input[input.Length-1].Content.Contains("后面在哪里使用"),"final answer waits for DSH evidence and preserves original question");return new ChatOutcome{Ok=true,Text="record uses iteration_details."};
    },true,null,messages.Add);
    Check(result.Ok&&calls==2&&client.Starts==1&&client.Cancels==0,"one real DSH job precedes the final answer");
    Check(client.Task.Contains(repo.Replace("\\","\\\\"))&&client.Task.Contains(commit)&&client.Task.Contains("step 里的 iteration_details")&&client.Task.Contains("不得修改")&&client.Task.Contains("追踪消费者"),"handoff carries pinned repo mapping, follow-up context and read-only constraints");
    Check(messages.Exists(s=>s.Contains("DSH 已完成")),"progress exposes source reading and answer synthesis phases");
    client=new Client();result=make(client).Complete(turns,input=>new ChatOutcome{Ok=true,Text="晚安"});Check(result.Ok&&client.Starts==0,"casual answers never submit DSH work");
    client=new Client();result=make(client).Complete(turns,Request,false);Check(!result.Ok&&client.Starts==0,"disabled DSH does not fall back to local source protocol");
    client=new Client();result=new DshSourceResearch(new LocalRepositorySettings(),new[]{url},"",client,()=>{}).Complete(turns,Request);Check(!result.Ok&&client.Starts==0&&result.Error.Contains("映射"),"missing local mapping fails before DSH submission");
    client=new Client();client.Result=new DshJob{Id="read-job",Status="error",Error="bridge unavailable"};calls=0;result=make(client).Complete(turns,input=>{calls++;return Request(input);});Check(!result.Ok&&calls==1,"failed DSH result never becomes a claimed source conclusion");
    client=new Client();client.Result.Tools=0;result=make(client).Complete(turns,Request);Check(!result.Ok,"tool-free DSH answer is not presented as verified reading");
    client=new Client();client.Result.Text="";result=make(client).Complete(turns,Request);Check(!result.Ok,"empty DSH conclusion cannot trigger synthesis");
    client=new Client();client.Result.Status="running";result=make(client).Complete(turns,Request);Check(!result.Ok&&client.Polls==2&&client.Cancels==1,"timed out source jobs are bounded and cancelled");
    client=new Client();result=make(client).Complete(turns,Request,true,()=>client.Starts>0);Check(!result.Ok&&client.Polls==0&&client.Cancels==1,"closing the owner cancels its source job without further polls");
    client=new Client();calls=0;result=make(client).Complete(turns,input=>{calls++;return new ChatOutcome{Ok=true,Text=calls==1?"[[SOURCE_READ]]{malformed legacy JSON":"done"};});Check(result.Ok&&client.Starts==1,"legacy history markers route to DSH without parsing generated JSON");
    client=new Client();calls=0;result=make(client).Complete(turns,input=>++calls==1?Request(input):new ChatOutcome{Ok=true,Text="[[DSH_TASK]]write files[[/DSH_TASK]]"});Check(!result.Ok&&client.Starts==1,"source synthesis cannot escalate into a write task");
    Check(Directory.GetFiles(Path.Combine(Storage.Folder,"dsh-source-research"),"*.jsonl").Length==1,"DSH job ids and result statuses are available for debugging");
   }finally{Storage.Folder=previous;}
  }
  static ChatOutcome Request(ChatTurn[] input){return new ChatOutcome{Ok=true,Text="[[DSH_READ]]读取定义与调用方[[/DSH_READ]]"};}
 }
}
