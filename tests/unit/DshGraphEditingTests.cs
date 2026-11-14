using System;
using System.Collections.Generic;
using System.IO;
namespace ElymsPet {
 static class DshGraphEditingTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("DSH graph edit: "+name);Console.WriteLine("PASS DSH graph edit: "+name);}
  sealed class Client:IDshSourceClient {
   public int Starts,Cancels;public string Task,Text;public Action BeforeResult;
   public DshJob Start(string task){Starts++;Task=task;return new DshJob{Id="edit-read",Status="running"};}
   public DshJob Poll(string id){if(BeforeResult!=null)BeforeResult();return new DshJob{Id=id,Status="done",Tools=3,Text=Text};}
   public bool Cancel(string id){Cancels++;return true;}
  }
  static ChatOutcome Ok(string text){return new ChatOutcome{Ok=true,Text=text};}
  static string Evidence(string url,bool verified=true){return GraphEditing.Json(new DshEditEvidence{Verified=verified,Findings="execute_model delegates to model_runner.execute_model.",Sources=new List<string>{url}});}
  public static void Run(string root){
   string old=Storage.Folder,repo=Path.Combine(root,"edit-source-repo");Directory.CreateDirectory(repo);
   try{
    Storage.Folder=Path.Combine(root,"edit-source-state");string url="https://github.com/org/repo/blob/"+new string('a',40)+"/worker.py#L10-L20";
    var settings=new LocalRepositorySettings{Repositories=new List<LocalRepositoryMapping>{new LocalRepositoryMapping{Repository="org/repo",Folder=repo}}};
    var turns=new[]{new ChatTurn("system","只返回 GraphEditRequest JSON。当前节点 worker。"),new ChatTurn("user","展开这个节点的实现和调用关系并保存")};
    var client=new Client{Text=Evidence(url)};int calls=0;var progress=new List<string>();
    var reader=new DshSourceResearch(settings,new[]{url},"worker",client,()=>{},2);
    var result=reader.CompleteEdit(turns,input=>{
     calls++;if(calls==1){Check(input[0].Content.Contains("补充或展开代码节点细节")&&input[1].Content.Contains("当前节点 worker"),"source triage receives the requested edit and node context");return Ok("{\"RequiresSource\":true,\"Question\":\"核实 execute_model 定义与调用方\"}");}
     string prompt=string.Join("\n",Array.ConvertAll(input,t=>t.Content));Check(client.Starts==1&&prompt.Contains("worker.py#L10-L20")&&prompt.Contains("GraphEditRequest JSON")&&!prompt.Contains("本轮本地源码读取结果"),"edit JSON generation waits for DSH evidence, without host source excerpts");return Ok("{\"Nodes\":[]}");
    },true,null,progress.Add);
    Check(result.Ok&&calls==2&&client.Starts==1&&client.Task.StartsWith("图谱编辑核实")&&client.Task.Contains("Verified")&&client.Task.Length<=8000,"source edit submits exactly one read-only evidence job: "+result.Error+" calls="+calls+" starts="+client.Starts);
    Check(progress.Exists(s=>s.Contains("正在整理节点修改")),"editing exposes read and synthesis progress");
    client=new Client();reader=new DshSourceResearch(settings,new[]{url},"",client,()=>{});calls=0;
    result=reader.CompleteEdit(turns,input=>++calls==1?Ok("{\"RequiresSource\":false}"):Ok("{\"Nodes\":[]}"),false);
    Check(result.Ok&&calls==2&&client.Starts==0,"text or structure edits can complete with DSH disabled");
    result=reader.CompleteEdit(turns,input=>Ok("{\"RequiresSource\":true,\"Question\":\"核实实现\"}"),false);
    Check(!result.Ok&&client.Starts==0&&result.Error.Contains("启用"),"source edits stop when DSH is disabled");
    foreach(string invalid in new[]{"{}","{\"RequiresSource\":null}","{\"RequiresSource\":true}","not json"}){
     calls=0;result=reader.CompleteEdit(turns,input=>{calls++;return Ok(invalid);});Check(!result.Ok&&calls==1&&client.Starts==0,"invalid triage cannot bypass source research: "+invalid);
    }
    reader=new DshSourceResearch(new LocalRepositorySettings(),new[]{url},"",client,()=>{});
    result=reader.CompleteEdit(turns,input=>Ok("{\"RequiresSource\":true,\"Question\":\"实现\"}"));Check(!result.Ok&&client.Starts==0&&result.Error.Contains("映射"),"missing mappings block factual edits");
    foreach(string invalid in new[]{Evidence(url,false),"无法读取源码",Evidence(url.Replace(new string('a',40),new string('b',40))),Evidence(url.Split('#')[0])}){
     client=new Client{Text=invalid};reader=new DshSourceResearch(settings,new[]{url},"",client,()=>{});calls=0;
     result=reader.CompleteEdit(turns,input=>{calls++;return Ok("{\"RequiresSource\":true,\"Question\":\"实现\"}");});Check(!result.Ok&&calls==1,"unverified, malformed or wrong-version evidence prevents patch generation");
    }
   }finally{Storage.Folder=old;}
   // Exercise the actual editor's atomic save and stale-revision checks after DSH.
   using(var h=new GraphAiCreationTests.Harness(true,root)){
    string url=h.Graph.Nodes.Find(n=>n.Id=="worker-npu").Sources,error;
    Check(LocalRepository.Save(new LocalRepositorySettings{Repositories=new List<LocalRepositoryMapping>{new LocalRepositoryMapping{Repository="vllm-project/vllm-ascend",Folder=repo}}},out error),"isolated source mapping saved");
    var client=new Client{Text=Evidence(url)};h.Api.RequiresSource=true;
    Func<GraphReadingSnapshot> capture=()=>GraphReadingSnapshot.Create(h.Service.Slot.Id,h.Graph,new GraphReadingView{Mode="module",NodeId="worker-npu",ModuleId="npu"},"补充实现细节",client,()=>{});
    h.SetAddResponse("dsh-added");GraphEditOutcome outcome=null;var statuses=new List<string>();h.Service.Changed+=()=>statuses.Add(h.Service.Status);
    h.Service.EditGraphWithAi("补充实现细节",capture(),r=>outcome=r);h.Queue.Run();
    Check(outcome.Success&&client.Starts==1&&h.Graph.Nodes.Exists(n=>n.Id=="dsh-added"),"actual node editor saves only after DSH finishes");
    Check(statuses.Exists(s=>s.Contains("正在思考")&&s.Contains("DSH 正在"))&&!h.Service.Busy,"actual editor publishes thinking progress and releases busy state");
    string before=GraphEditing.Revision(h.Graph);client.Text=Evidence(url,false);h.SetAddResponse("must-not-save");h.Service.EditGraphWithAi("核实后补充",capture(),r=>outcome=r);h.Queue.Run();
    Check(!outcome.Success&&before==GraphEditing.Revision(h.Graph)&&outcome.Message.Contains("源码结论"),"failed evidence preserves the graph and surfaces the actual reason");
    client.Text=Evidence(url);client.BeforeResult=()=>h.Graph.Nodes[0].Summary="concurrent update";h.SetAddResponse("stale-dsh-patch");h.Service.EditGraphWithAi("补充细节",capture(),r=>outcome=r);h.Queue.Run();
    Check(!outcome.Success&&!h.Graph.Nodes.Exists(n=>n.Id=="stale-dsh-patch"),"concurrent graph changes during DSH cannot be overwritten");
   }
  }
 }
}
