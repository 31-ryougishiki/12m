using System;
using System.Collections.Generic;
using System.IO;
namespace ElymsPet {
 static class DomainGraphTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Domain graph: "+name);Console.WriteLine("PASS domain: "+name);}
  static GraphEditRequest Patch(DomainGraph g,string id,string summary){return GraphEditing.Parse<GraphEditRequest>("{\"RequestId\":\""+id+"\",\"TopicId\":\""+g.Id+"\",\"ExpectedRevision\":\""+GraphEditing.Revision(g)+"\",\"Nodes\":[{\"Id\":\"mc2\",\"Summary\":\""+summary+"\"}]}");}
  public static void Run(string root){
   string old=Storage.Folder;Storage.Folder=Path.Combine(root,"domain-graph");
   try{
    var book=ArchiveStore.Normalize(new ArchiveBook());var slot=book.Slots[0];slot.DomainGraphs=null;slot.AppliedGraphEdits=null;ArchiveStore.Normalize(book);
    Check(slot.DomainGraphs.Count==0&&slot.Items.Count==0,"legacy archive gains no fabricated personal mastery");
    Check(DomainKnowledge.List(slot).Count==0,"empty archive does not recreate removed built-in topic");
    var seed=Fixture();slot.DomainGraphs.Add(seed);
    var repo=new TestRepo(book);using(var service=new ArchiveService(new PetState(),new Scheduler(),new PetFeedback(delegate{},delegate{},delegate{}),repo,true)){
     string message;var patch=Patch(seed,"edit-1","补充后的介绍");string code=seed.Nodes.Find(delegate(DomainNode n){return n.Id=="mc2";}).Code;
     Check(service.ApplyGraphEdit(slot,patch,out message),"AI / DSH partial node edit saves");
     var saved=DomainKnowledge.List(slot)[0];Check(saved.Nodes.Find(delegate(DomainNode n){return n.Id=="mc2";}).Code==code,"omitted fields preserve code");
     Check(service.ApplyGraphEdit(slot,patch,out message)&&slot.AppliedGraphEdits.Count==1,"replayed request is idempotent");
     patch.RequestId="stale";Check(!service.ApplyGraphEdit(slot,patch,out message)&&message.Contains("Revision"),"stale edit cannot overwrite a newer graph");
     var invalid=Patch(saved,"invalid","bad");invalid.Edges=new List<DomainEdge>{new DomainEdge{From="mc2",To="missing",Label="依赖"}};
     Check(!service.ApplyGraphEdit(slot,invalid,out message)&&DomainKnowledge.List(slot)[0].Nodes.Find(delegate(DomainNode n){return n.Id=="mc2";}).Summary=="补充后的介绍","invalid relation rolls back whole request");
     repo.Fail=true;Check(!service.ApplyGraphEdit(slot,Patch(saved,"fail-save","未保存"),out message)&&DomainKnowledge.List(slot)[0].Revision==saved.Revision,"failed repository save restores graph and revision");repo.Fail=false;
     var cloud=ArchiveCloudCodec.Decode(ArchiveCloudCodec.Encode(book));Check(cloud.Slots[0].DomainGraphs[0].Nodes.Find(delegate(DomainNode n){return n.Id=="mc2";}).Code==code,"cloud roundtrip retains rich node code");
     ArchiveKnowledge.WriteFiles(slot);var exported=GraphEditing.Parse<KnowledgeGraphExport>(File.ReadAllText(Path.Combine(ArchiveKnowledge.Folder(slot),"graph.json")));Check(exported.Topics[0].Revision==saved.Revision&&File.Exists(Path.Combine(ArchiveKnowledge.Folder(slot),"GRAPH_EDIT.md")),"DSH snapshot includes revision and editing protocol");
     service.SaveKnowledgeNode("","基础","本地","待核实","了解","依据","备注");var personal=DomainKnowledge.List(slot).Find(delegate(DomainGraph g){return g.Id=="archive:本地";});personal.Nodes[0].Tier="S";service.SaveDomainGraph(personal);service.SaveKnowledgeNode("","新增","本地","待核实","了解","依据","备注");
     Check(DomainKnowledge.List(slot).Find(delegate(DomainGraph g){return g.Id=="archive:本地";}).Nodes.Count==2,"edited personal domain still receives future interview knowledge");
     var architecture=RepositoryArchitectureTests.Fixture();architecture.Architecture=RepositoryArchitecture.Build(architecture);
     var mapRequest=new GraphEditRequest{RequestId="map-import",TopicId=architecture.Id,Title=architecture.Title,ExpectedRevision="",Nodes=architecture.Nodes,Edges=architecture.Edges,Architecture=architecture.Architecture};
     Check(service.ApplyGraphEdit(slot,mapRequest,out message),"architecture metadata imports through the validated graph editing API");
     var importedMap=DomainKnowledge.List(slot).Find(g=>g.Id==architecture.Id);var invalidMap=DomainKnowledge.Clone(importedMap).Architecture;invalidMap.Journey[0].NodeId="missing";
     Check(!service.ApplyGraphEdit(slot,new GraphEditRequest{RequestId="bad-map",TopicId=architecture.Id,ExpectedRevision=GraphEditing.Revision(importedMap),Architecture=invalidMap},out message)&&GraphEditing.Revision(DomainKnowledge.List(slot).Find(g=>g.Id==architecture.Id))==GraphEditing.Revision(importedMap),"invalid architecture update is atomic and preserves the existing map");

    }
    var scheduler=new QueueScheduler();var api=new Api();var disk=new JsonRepository<ArchiveBook>(Path.Combine(Storage.Folder,"archives.json"));disk.Save(book);
    using(var service=new ArchiveService(new PetState{AiChat=true,ApiKey="test-key"},scheduler,new PetFeedback(delegate{},delegate{},delegate{}),disk,false,api)){
     service.CurrentDomainId="test-domain";var active=service.Slot;var current=DomainKnowledge.List(active).Find(delegate(DomainGraph g){return g.Id=="test-domain";});string path=Path.Combine(ArchiveKnowledge.Folder(active),"graph-edit.json");
     File.WriteAllText(path,GraphEditing.Json(Patch(current,"file-request","来自 DSH 的补充")));service.PollGraphEdits();
     var receipt=GraphEditing.Parse<GraphEditReceipt>(File.ReadAllText(Path.Combine(ArchiveKnowledge.Folder(active),"graph-edit-result.json")));
     Check(receipt.Status=="applied"&&receipt.RequestId=="file-request"&&disk.Load().Slots[0].DomainGraphs.Find(delegate(DomainGraph g){return g.Id=="test-domain";}).Nodes.Find(delegate(DomainNode n){return n.Id=="mc2";}).Summary=="来自 DSH 的补充","DSH file request persists and returns matching applied receipt");
     current=DomainKnowledge.List(active).Find(delegate(DomainGraph g){return g.Id=="test-domain";});api.Result=GraphEditing.Json(Patch(current,"ai-request","曳尔沐的补充"));string answer=null;
     service.EditGraphWithAi("补充 MC2 的介绍",delegate(string message){answer=message;});scheduler.Run();
     Check(answer!=null&&answer.Contains("已保存")&&api.Prompt.Contains("enable_fused_mc2")&&DomainKnowledge.List(active).Find(delegate(DomainGraph g){return g.Id=="test-domain";}).Nodes.Find(delegate(DomainNode n){return n.Id=="mc2";}).Summary=="曳尔沐的补充","AI receives actual node context and applies validated response");
     api.Result="测试讲解";answer=null;var beforeExplain=GraphEditing.Revision(current);service.ExplainRepositoryNode(current,current.Nodes[0],"解释代码",delegate(string message){answer=message;});scheduler.Run();Check(answer!=null&&answer.Contains("待核实")&&!service.Busy&&GraphEditing.Revision(current)==beforeExplain,"tutor returns unverified answer without changing graph");
     var learningPatch=Patch(DomainKnowledge.List(active).Find(delegate(DomainGraph g){return g.Id=="test-domain";}),"learning-import","学习说明");learningPatch.Nodes[0].Learning=new NodeLearning{Reflection="AI 代填",Evidence=new List<LearningEvidence>{new LearningEvidence{Reviewed=true,Url="https://example.org",Version="v1",Excerpt="e",Reason="r"}}};Check(service.ApplyGraphEdit(active,learningPatch,out answer),"learning metadata imports");var learned=DomainKnowledge.List(active).Find(delegate(DomainGraph g){return g.Id=="test-domain";}).Nodes.Find(delegate(DomainNode n){return n.Id=="mc2";});Check(!learned.Learning.Evidence[0].Reviewed&&learned.Learning.Reflection=="","AI cannot certify evidence or invent user reflection");
     api.Throw=true;answer=null;service.EditGraphWithAi("补充",delegate(string message){answer=message;});scheduler.Run();Check(answer!=null&&!service.Busy,"AI transport exception resolves callback without leaving chat busy");
    }
    string reply="处理一下 [[GRAPH_EDIT]]把 MC2 改为核心[[/GRAPH_EDIT]]";Check(GraphEditing.Extract(ref reply)=="把 MC2 改为核心"&&!reply.Contains("GRAPH_EDIT"),"chat marker extracted without leaking control syntax");
    var migrated=new PetState{RadialPages="Companion,Archive",DomainGraphEntryAdded=false};migrated.Normalize();Check(migrated.RadialPages.Contains("Graph"),"existing wheel gains direct graph entry");migrated.RadialPages=RadialMenuLayout.Toggle(migrated.RadialPages,PanelTab.Graph,false);migrated.Normalize();Check(!migrated.RadialPages.Contains("Graph"),"user can hide graph after migration");
   }finally{Storage.Folder=old;}
  }
  public static DomainGraph Fixture(){
   var g=new DomainGraph{Id="test-domain",Title="图谱测试领域"};
   g.Nodes.Add(new DomainNode{Id="mc2",Title="MC2 测试",Tier="S",Summary="合成节点",Approach="测试数据",Code="enable_fused_mc2",Sources="https://example.org/test"});
   g.Nodes.Add(new DomainNode{Id="aclgraph",Title="ACLGraph 测试",Tier="CORE",Summary="合成节点",Code="--enforce-eager"});
   g.Nodes.Add(new DomainNode{Id="config",Title="配置测试",Tier="A",Summary="合成节点"});
   g.Edges.Add(new DomainEdge{From="mc2",To="config",Label="依赖"});return g;
  }
  sealed class TestRepo:IRepository<ArchiveBook>{ArchiveBook book;public bool Fail;public TestRepo(ArchiveBook b){book=b;}public ArchiveBook Load(){return book;}public bool Save(ArchiveBook b){return !Fail;}public string LastError{get{return "simulated failure";}}}
  sealed class Scheduler:IWorkScheduler{public void Queue(Action a){throw new Exception("Unexpected network");}public void Post(Action a){a();}}
  sealed class QueueScheduler:IWorkScheduler{readonly Queue<Action> jobs=new Queue<Action>();public void Queue(Action a){jobs.Enqueue(a);}public void Post(Action a){a();}public void Run(){while(jobs.Count>0)jobs.Dequeue()();}}
  sealed class Api:IArchiveApi{public string Result,Prompt;public bool Throw;public ChatOutcome Complete(string key,string url,string model,bool thinking,ChatTurn[] turns,int timeout,int tokens,double temperature){Prompt=turns[0].Content;if(Throw)throw new IOException("simulated transport failure");if(Prompt==DshSourceResearch.EditPlanRules)return new ChatOutcome{Ok=true,Text="{\"RequiresSource\":false}"};return new ChatOutcome{Ok=true,Text=Result};}public WebOutcome Search(string a,string b,string c){throw new Exception("No search");}public WebOutcome Fetch(string a,string b,string c){throw new Exception("No fetch");}}
 }
}

