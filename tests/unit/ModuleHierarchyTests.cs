using System;
using System.Collections.Generic;
namespace ElymsPet {
 public static class ModuleHierarchyTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Module hierarchy: "+name);Console.WriteLine("PASS hierarchy: "+name);}
  public static GraphEditRequest Split(DomainGraph g){return new GraphEditRequest{
   RequestId=Guid.NewGuid().ToString("N"),TopicId=g.Id,ExpectedRevision=GraphEditing.Revision(g),
   Modules=new List<GraphModulePatch>{
    new GraphModulePatch{Id="npu-model",Title="模型执行",Repository="vLLM-Ascend",ParentId="npu",Summary="模型执行学习分组"},
    new GraphModulePatch{Id="npu-forward",Title="前向与输出处理",Repository="vLLM-Ascend",ParentId="npu-model",Summary="已有 Runner 节点的学习分组，非新增源码事实"},
    new GraphModulePatch{Id="npu-input",Title="输入准备与请求数据整理：较长的模块名称用于检查文字换行和导航排版",Repository="vLLM-Ascend",ParentId="npu-model",Summary="待补充源码，暂时为空"}},
   NodeModules=new List<GraphNodeModule>{new GraphNodeModule{NodeId="ascend-model-runner",ModuleId="npu-forward"}}
  };}
  public static void Run(string root){
   using(var h=new GraphAiCreationTests.Harness(true,root)){
    string nodes=GraphEditing.Json(h.Graph.Nodes),edges=GraphEditing.Json(h.Graph.Edges),journey=GraphEditing.Json(h.Graph.Architecture.Journey);
    h.Api.Result=GraphEditing.Json(Split(h.Graph));GraphEditOutcome result=null;
    h.Service.EditGraphWithAi("在当前模块创建两级子模块，将模型执行节点移入前向与输出处理",h.Graph.Id,"","npu",r=>result=r);h.Queue.Run();
    Check(result.Success&&result.UpdatedModuleIds.Count==3&&result.AddedNodeIds.Count==0,"AI module-only edit creates two nested levels and returns module navigation IDs");
    var map=h.Graph.Architecture;Check(RepositoryArchitecture.PathLabel(map,"npu-forward")=="NPU 执行层 › 模型执行 › 前向与输出处理"&&RepositoryArchitecture.Children(map,"npu-model").Count==2,"parent links provide ordered breadcrumbs and direct children");
    Check(RepositoryArchitecture.Members(map,"npu").Count==2&&RepositoryArchitecture.Parent(map,"ascend-model-runner").Id=="npu-forward","descendants are counted once and node ownership stays direct");
    Check(RepositoryArchitecture.VisibleItem(map,"npu","ascend-model-runner")=="@architecture/npu-model"&&RepositoryArchitecture.VisibleItem(map,"npu","worker-npu")=="worker-npu"&&RepositoryArchitecture.VisibleItem(map,"","ascend-model-runner")=="@architecture/npu","relationships project to visible child cards at each depth");
    Check(nodes==GraphEditing.Json(h.Graph.Nodes)&&edges==GraphEditing.Json(h.Graph.Edges)&&journey==GraphEditing.Json(map.Journey),"reorganizing modules preserves source descriptions, edges and request journey");
    Check(h.Api.Prompt.Contains("ParentId")&&h.Api.Prompt.Contains("默认新增节点归属模块=npu"),"AI receives incremental hierarchy protocol and exact scope");
    var reading=GraphReadingSnapshot.Create(h.Service.Slot.Id,h.Graph,new GraphReadingView{Mode="module",ModuleId="npu-model"},"解释这个模块");
    Check(reading.Text.Contains("ParentId=npu-model")&&reading.Text.Contains("NPU 执行层 › 模型执行")&&reading.CodeUrls.Count>0,"consultation sees hierarchy path and representative descendant source");
    Check(RepositoryArchitecture.Validate(DomainKnowledge.Clone(h.Graph),DomainKnowledge.Clone(h.Graph).Architecture)=="","hierarchy survives persistence roundtrip");
    foreach(var bad in new[]{new GraphModulePatch{Id="npu",ParentId="npu-forward"},new GraphModulePatch{Id="npu-model",ParentId="missing"},new GraphModulePatch{Id="npu-model",ParentId="engine"},new GraphModulePatch{Id="npu-model",ParentId="npu-model"}}){
     string before=GraphEditing.Revision(h.Graph),error;var patch=new GraphEditRequest{RequestId=Guid.NewGuid().ToString("N"),TopicId=h.Graph.Id,ExpectedRevision=before,Modules=new List<GraphModulePatch>{bad}};
     Check(!h.Service.ApplyGraphEdit(h.Service.Slot,patch,out error)&&before==GraphEditing.Revision(h.Graph),"cycle, missing parent, cross-repository or self-parent edit is atomic: "+bad.ParentId);
    }
    var move=new GraphEditRequest{RequestId=Guid.NewGuid().ToString("N"),TopicId=h.Graph.Id,ExpectedRevision=GraphEditing.Revision(h.Graph),Modules=new List<GraphModulePatch>{new GraphModulePatch{Id="npu-model",Title="模型运行",ParentId=""}}};string message;
    Check(h.Service.ApplyGraphEdit(h.Service.Slot,move,out message)&&RepositoryArchitecture.Path(h.Graph.Architecture,"npu-forward").Count==2&&RepositoryArchitecture.Children(h.Graph.Architecture,"").Exists(m=>m.Id=="npu-model"),"rename and move-to-root retain the whole subtree");
    var deep=DomainKnowledge.Clone(h.Graph);string parent="npu-forward";for(int i=0;i<12;i++){string id="deep-"+i;deep.Architecture.Modules.Add(new RepositoryModule{Id=id,Title=id,Repository="vLLM-Ascend",ParentId=parent});parent=id;}
    Check(RepositoryArchitecture.Validate(deep,deep.Architecture).Contains("12"),"excessive nesting is rejected before rendering");
    move.RequestId=Guid.NewGuid().ToString("N");move.ExpectedRevision=GraphEditing.Revision(h.Graph);move.Modules[0].Title="不应保存";string revision=GraphEditing.Revision(h.Graph);h.Repo.Fail=true;
    Check(!h.Service.ApplyGraphEdit(h.Service.Slot,move,out message)&&GraphEditing.Revision(h.Graph)==revision,"storage failure rolls back module changes");h.Repo.Fail=false;
    move.RequestId=Guid.NewGuid().ToString("N");move.ExpectedRevision="stale";Check(!h.Service.ApplyGraphEdit(h.Service.Slot,move,out message),"module-only edits also require the current graph revision");
   }
   using(var h=new GraphAiCreationTests.Harness(false,root)){
    var patch=Split(h.Graph);patch.Modules[0].ParentId=null;h.Api.Result=GraphEditing.Json(patch);GraphEditOutcome result=null;h.Service.EditGraphWithAi("新增子模块",h.Graph.Id,"","npu",r=>result=r);h.Queue.Run();
    Check(result.Success&&h.Graph.Architecture.Modules.Find(m=>m.Id=="npu-model").ParentId=="npu","legacy flat projection materializes and omitted new parent inherits scope");
   }
  }
 }
}
