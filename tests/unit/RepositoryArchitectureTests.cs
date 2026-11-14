using System;
using System.Collections.Generic;
namespace ElymsPet {
 static class RepositoryArchitectureTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Architecture: "+name);Console.WriteLine("PASS architecture: "+name);}
  public static void Run(){
   var g=Fixture();string before=GraphEditing.Json(g);var map=RepositoryArchitecture.Build(g);
   Check(map!=null&&map.Modules.Count==11&&map.Journey.Count==8,"source topic projects into repository modules and an eight-step journey");
   Check(GraphEditing.Json(g)==before&&g.Architecture==null,"projection never rewrites source nodes, notes or archive metadata");
   Check(RepositoryArchitecture.Validate(g,map)=="","every source node belongs to exactly one module");
   Check(map.Journey[1].NodeId==map.Journey[6].NodeId&&map.Journey[1].Title!=map.Journey[6].Title,"engine request and result phases remain distinct steps");
   Check(RepositoryArchitecture.Build(DomainGraphTests.Fixture())==null&&RepositoryArchitecture.Build(RepositoryLearningExample.Create())==null,"ordinary graphs and the four-step learning sequence keep their original view");
   var unknown=new DomainNode{Id="new-symbol",Title="补充节点",Summary="还没有经过架构分类",Sources=g.Nodes[0].Sources};g.Nodes.Add(unknown);map=RepositoryArchitecture.Build(g);
   Check(RepositoryArchitecture.Parent(map,unknown.Id).Id=="other","unknown symbols remain visible in an explicit unclassified module");
   g=Fixture();g.Nodes[0].Sources=g.Nodes[0].Sources.Replace(RepositoryArchitecture.VllmRevision,new string('a',40));Check(RepositoryArchitecture.Build(g).Journey.Count==0,"a different source revision cannot claim the curated execution route");
   g=Fixture();g.Architecture=RepositoryArchitecture.Build(g);var copy=DomainKnowledge.Clone(g);Check(copy.Architecture.Journey.Count==8&&RepositoryArchitecture.Validate(copy,copy.Architecture)=="","explicit maps survive archive serialization");
   copy.Architecture.Modules[1].Nodes.Add(copy.Architecture.Modules[0].Nodes[0]);Check(RepositoryArchitecture.Validate(copy,copy.Architecture).Length>0,"duplicate ownership is rejected");
   copy=DomainKnowledge.Clone(g);copy.Architecture.Journey[0].NodeId="missing";Check(RepositoryArchitecture.Validate(copy,copy.Architecture).Length>0,"journey cannot point to a missing source node");
  }
  public static DomainGraph Fixture(){
   var g=new DomainGraph{Id="architecture-fixture",Title="vLLM / Ascend · 架构学习"};
   Add(g,"entry-async-llm","AsyncLLM：接收生成请求","vllm/v1/engine/async_llm.py",411,false);
   Add(g,"entry-output-loop","输出回流协程","vllm/v1/engine/async_llm.py",870,false);
   Add(g,"engine-core-step","EngineCore：调度与更新","vllm/v1/engine/core.py",718,false);
   Add(g,"scheduler-schedule","Scheduler：token 预算","vllm/v1/core/sched/scheduler.py",685,false);
   Add(g,"executor-boundary","Executor：派发执行","vllm/v1/executor/abstract.py",53,false);
   Add(g,"worker-gpu","Worker：设备管理","vllm/v1/worker/gpu_worker.py",216,false);
   Add(g,"model-registry","模型注册与选择","vllm/model_executor/models/registry.py",1,false);
   Add(g,"platform-interface","Platform：设备接口","vllm/platforms/interface.py",137,false);
   Add(g,"platform-npu","NPUPlatform：接入钩子","vllm_ascend/platform.py",101,true);
   Add(g,"worker-npu","NPUWorker：执行入口","vllm_ascend/worker/worker.py",858,true);
   Add(g,"ascend-model-runner","NPUModelRunner：模型执行","vllm_ascend/worker/v2/model_runner.py",617,true);
   Add(g,"attn-backend-select","Attention：后端选择","vllm_ascend/platform.py",270,true);
   Add(g,"aclgraph-integration","ACLGraph：捕获与回放","vllm_ascend/compilation/acl_graph.py",56,true);
   Add(g,"hccl-parallel","HCCL：分布式通信","vllm_ascend/worker/worker.py",1414,true);
   g.Edges.Add(new DomainEdge{From="entry-async-llm",To="engine-core-step",Label="经 IPC 提交",Evidence="跨进程请求不是直接调用 step"});
   g.Edges.Add(new DomainEdge{From="engine-core-step",To="scheduler-schedule",Label="调用",Evidence="core.py:727"});
   g.Edges.Add(new DomainEdge{From="platform-npu",To="platform-interface",Label="继承与覆盖",Evidence="平台接口"});
   g.Edges.Add(new DomainEdge{From="worker-npu",To="ascend-model-runner",Label="委托执行",Evidence="worker.py:892"});return g;
  }
  static void Add(DomainGraph graph,string id,string title,string path,int line,bool ascend){graph.Nodes.Add(new DomainNode{Id=id,Title=title,Summary="源码定位与职责学习 · 测试样例",Sources="https://github.com/vllm-project/"+(ascend?"vllm-ascend":"vllm")+"/blob/"+(ascend?RepositoryArchitecture.AscendRevision:RepositoryArchitecture.VllmRevision)+"/"+path+"#L"+line});}
 }
}
