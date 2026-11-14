using System;
using System.Collections.Generic;
using System.IO;
namespace ElymsPet {
 public static class GraphAiCreationTests {
  static void Check(bool ok,string message){if(!ok)throw new Exception("AI node creation: "+message);Console.WriteLine("PASS AI graph: "+message);}
  public static void Run(string root){
   foreach(bool explicitMap in new[]{false,true})using(var h=new Harness(explicitMap,root)){
    var original=h.Graph;string nodes=GraphEditing.Json(original.Nodes),journey=GraphEditing.Json(RepositoryArchitecture.Build(original).Journey);h.SetAddResponse("added-a","added-b");GraphEditOutcome result=null;
    h.Service.EditGraphWithAi("新增两个节点并关联当前节点",original.Id,"worker-npu","npu",delegate(GraphEditOutcome r){result=r;});Check(h.Service.Busy,"request enters busy state");h.Queue.Run();
    var saved=h.Graph;Check(result!=null&&result.Success&&result.AddedNodeIds.Count==2&&!h.Service.Busy,"scoped AI creates multiple nodes: "+explicitMap);
    Check(RepositoryArchitecture.Parent(saved.Architecture,"added-a").Id=="npu"&&RepositoryArchitecture.Parent(saved.Architecture,"added-b").Id=="npu","new nodes inherit the current module even when the model omits placement");
    Check(GraphEditing.Json(saved.Nodes.FindAll(n=>n.Id!="added-a"&&n.Id!="added-b"))==nodes&&GraphEditing.Json(saved.Architecture.Journey)==journey,"existing content and curated journey survive additions");
    Check(saved.Edges.Exists(e=>e.From=="worker-npu"&&e.To=="added-a")&&h.Api.Prompt.Contains("默认新增节点归属模块=npu")&&h.Api.Prompt.Contains("NodeModules"),"model receives module context and new relationships persist");
    h.SetAddResponse("elsewhere");var patch=GraphEditing.Parse<GraphEditRequest>(h.Api.Result);patch.NodeModules=new List<GraphNodeModule>{new GraphNodeModule{NodeId="elsewhere",ModuleId="cache"}};h.Api.Result=GraphEditing.Json(patch);
    h.Service.EditGraphWithAi("新增到调度模块",saved.Id,"worker-npu","npu",r=>result=r);h.Queue.Run();Check(result.Success&&RepositoryArchitecture.Parent(h.Graph.Architecture,"elsewhere").Id=="cache","explicit valid module assignment overrides the default");
   }
   using(var h=new Harness(true,root)){
    h.SetAddResponse("unassigned-node");string message;Check(h.Service.ApplyGraphEdit(h.Service.Slot,GraphEditing.Parse<GraphEditRequest>(h.Api.Result),out message)&&RepositoryArchitecture.Parent(h.Graph.Architecture,"unassigned-node").Title=="待归类节点","external additions without placement remain visible in an explicit map");
    string before=GraphEditing.Revision(h.Graph);h.SetAddResponse("bad-placement");var patch=GraphEditing.Parse<GraphEditRequest>(h.Api.Result);patch.NodeModules=new List<GraphNodeModule>{new GraphNodeModule{NodeId="bad-placement",ModuleId="missing"}};
    Check(!h.Service.ApplyGraphEdit(h.Service.Slot,patch,out message)&&GraphEditing.Revision(h.Graph)==before,"invalid module rejects nodes and edges atomically");
    h.SetAddResponse("cross-topic");patch=GraphEditing.Parse<GraphEditRequest>(h.Api.Result);patch.TopicId="another-topic";patch.ExpectedRevision="";patch.Title="Wrong topic";h.Api.Result=GraphEditing.Json(patch);GraphEditOutcome result=null;
    h.Service.EditGraphWithAi("新增",h.Graph.Id,"worker-npu","npu",r=>result=r);h.Queue.Run();Check(!result.Success&&GraphEditing.Revision(h.Graph)==before&&h.Service.Slot.DomainGraphs.Count==1,"node entry cannot write a different topic returned by the model");
    h.SetAddResponse("stale");h.Service.EditGraphWithAi("新增",h.Graph.Id,"worker-npu","npu",r=>result=r);h.Service.Slot.DomainGraphs[0].Nodes[0].Summary="并发更新";h.Queue.Run();Check(!result.Success&&!h.Graph.Nodes.Exists(n=>n.Id=="stale"),"concurrent revision change rejects stale additions");
    h.SetAddResponse("not-saved");h.Repo.Fail=true;h.Service.EditGraphWithAi("新增",h.Graph.Id,"worker-npu","npu",r=>result=r);h.Queue.Run();h.Repo.Fail=false;Check(!result.Success&&result.AddedNodeIds.Count==0&&!h.Graph.Nodes.Exists(n=>n.Id=="not-saved"),"save failure never reports or navigates to an unsaved node");
    h.Api.Result="{broken";h.Service.EditGraphWithAi("新增",h.Graph.Id,"worker-npu","npu",r=>result=r);h.Queue.Run();Check(!result.Success&&!h.Service.Busy,"malformed model response releases busy state");
    h.SetAddResponse("truncated");h.Api.Truncated=true;h.Service.EditGraphWithAi("新增",h.Graph.Id,"worker-npu","npu",r=>result=r);h.Queue.Run();Check(!result.Success&&!h.Graph.Nodes.Exists(n=>n.Id=="truncated"),"truncated response cannot partially save nodes");
   }
  }
  public sealed class Harness:IDisposable {
   public readonly QueueScheduler Queue=new QueueScheduler();public readonly FakeApi Api=new FakeApi();public readonly MemoryRepo Repo;public readonly ArchiveService Service;
   public DomainGraph Graph {get{return DomainKnowledge.List(Service.Slot).Find(g=>g.Id=="architecture-fixture");}}
   readonly string previousFolder;
   public Harness(bool explicitMap,string root){previousFolder=Storage.Folder;Storage.Folder=Path.Combine(root,"ai-graph-"+Guid.NewGuid().ToString("N"));var book=ArchiveStore.Normalize(new ArchiveBook());var graph=RepositoryArchitectureTests.Fixture();if(explicitMap)graph.Architecture=RepositoryArchitecture.Build(graph);book.Slots[0].DomainGraphs.Add(graph);Repo=new MemoryRepo(book);Service=new ArchiveService(new PetState{AiChat=true,ApiKey="test-key"},Queue,new PetFeedback(delegate{},delegate{},delegate{}),Repo,false,Api);}
   public void SetAddResponse(params string[] ids){var graph=Graph;var request=new GraphEditRequest{RequestId=Guid.NewGuid().ToString("N"),TopicId=graph.Id,ExpectedRevision=GraphEditing.Revision(graph),Nodes=new List<DomainNode>(),Edges=new List<DomainEdge>()};foreach(string id in ids){request.Nodes.Add(new DomainNode{Id=id,Title="新增学习节点 "+id,Summary="由测试替身补充的输入与输出说明",Status="AI 整理 · 待核实"});request.Edges.Add(new DomainEdge{From="worker-npu",To=id,Label="学习延伸",Evidence="合成测试，不声称调用关系"});}Api.Result=GraphEditing.Json(request);}
   public void Dispose(){Service.Dispose();Storage.Folder=previousFolder;}
  }
  public sealed class MemoryRepo:IRepository<ArchiveBook>{readonly ArchiveBook book;public bool Fail;public MemoryRepo(ArchiveBook value){book=value;}public ArchiveBook Load(){return book;}public bool Save(ArchiveBook value){return !Fail;}public string LastError{get{return "simulated failure";}}}
  public sealed class QueueScheduler:IWorkScheduler{readonly Queue<Action> jobs=new Queue<Action>();public void Queue(Action action){jobs.Enqueue(action);}public void Post(Action action){action();}public void Run(){while(jobs.Count>0)jobs.Dequeue()();}}
  public sealed class FakeApi:IArchiveApi{public string Result,Prompt;public bool Truncated,RequiresSource;public int Calls,PlanCalls;public readonly Queue<string> Results=new Queue<string>();public ChatOutcome Complete(string key,string url,string model,bool thinking,ChatTurn[] turns,int timeout,int tokens,double temperature){Calls++;Prompt=turns[0].Content;if(Prompt==DshSourceResearch.EditPlanRules){PlanCalls++;return new ChatOutcome{Ok=true,Text=GraphEditing.Json(new DshEditPlan{RequiresSource=RequiresSource,Question="核实实现和调用方"})};}return new ChatOutcome{Ok=true,Text=Results.Count>0?Results.Dequeue():Result,Truncated=Truncated};}public WebOutcome Search(string a,string b,string c){throw new Exception("No network in test");}public WebOutcome Fetch(string a,string b,string c){throw new Exception("No network in test");}}
 }
}
