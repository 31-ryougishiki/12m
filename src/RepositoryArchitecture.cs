using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
namespace ElymsPet {
 [DataContract] public sealed class RepositoryModule {
  [DataMember] public string Id="",Title="",Repository="",Summary="";
  [DataMember(EmitDefaultValue=false)] public string ParentId;
  [DataMember] public List<string> Nodes=new List<string>();
 }
 [DataContract] public sealed class RepositoryStep {
  [DataMember] public string NodeId="",Title="",Boundary="",Input="",Output="",Summary="";
 }
 [DataContract] public sealed class RepositoryMap {
  [DataMember] public string Scope="";
  [DataMember] public List<RepositoryModule> Modules=new List<RepositoryModule>();
  [DataMember] public List<RepositoryStep> Journey=new List<RepositoryStep>();
 }
 // A read-only projection: adding a map must never rewrite notes, evidence or source nodes.
 public static class RepositoryArchitecture {
  public const string VllmRevision="c021a8b0a9049c39db7142823126b1c28a4fb85b",AscendRevision="e4d533d33211f0696bcdf65eda47245d52e428c5";
  public static RepositoryMap Build(DomainGraph graph){
   if(graph==null)return null;
   if(graph.Architecture!=null)return Validate(graph,graph.Architecture).Length==0?graph.Architecture:null;
   var map=new RepositoryMap{Scope="按已有源码节点组织的架构导览；未覆盖全部仓库。两个提交独立采集，不代表已验证的兼容版本组合。"};
   var modules=new[]{
    Module("entry","入口与输出","vLLM","接收生成请求，把引擎输出交还调用者。"),
    Module("engine","引擎与执行调度","vLLM","组织调度、派发执行和结果更新的一轮闭环。"),
    Module("cache","调度与 KV 缓存","vLLM","分配 token 预算，管理缓存块与请求状态。"),
    Module("execution","GPU 执行基准","vLLM","Worker 管理设备，Model Runner 组织模型执行。"),
    Module("model","模型注册与实现","vLLM","从架构名称找到模型类，再装载和执行模型。"),
    Module("interface","平台接口","vLLM","为设备、后端与编译等能力提供扩展钩子。"),
    Module("platform","注册与平台适配","vLLM-Ascend","注册 NPU 平台，按配置接入实现和补丁。"),
    Module("npu","NPU 执行层","vLLM-Ascend","NPU Worker 与 Runner 承接设备初始化和执行。"),
    Module("attention","Attention 后端","vLLM-Ascend","按设备与模型条件选择注意力实现。"),
    Module("compile","编译与图执行","vLLM-Ascend","编译、捕获和回放设备计算图。"),
    Module("parallel","并行与 MoE","vLLM-Ascend","通信组、专家通信和负载均衡协同工作。")};
   var known=new Dictionary<string,string>();
   Bind(known,"entry","entry-async-llm","entry-output-loop");Bind(known,"engine","engine-core-step","engine-core-kv-init","executor-boundary");
   Bind(known,"cache","scheduler-schedule","scheduler-preempt","kv-block-alloc","kv-prefix-cache","scheduler","kv");
   Bind(known,"execution","model-runner-execute","model-runner-load","worker-gpu");Bind(known,"model","model-registry","model-registry-table","model-impl-deepseek");
   Bind(known,"interface","platform-interface");Bind(known,"platform","platform-npu","plugin-registration","patch-layer","ascend-scheduler-cls","kv-config-builder","platform");
   Bind(known,"npu","worker-npu","ascend-model-runner");Bind(known,"attention","attn-backend-select","legacy-310p","attention");
   Bind(known,"compile","aclgraph-integration","compile-backend");Bind(known,"parallel","hccl-parallel","moe-comm","eplb");
   int upstream=0,ascend=0;var others=new RepositoryModule{Id="other",Title="其他已有节点",Repository="补充知识",Summary="尚未建立架构归属；保留原有内容与关联。"};
   foreach(var node in graph.Nodes){
    string repo=SourceRepository(node),key;
    if(repo.Length==0){others.Nodes.Add(node.Id);continue;}
    if(repo=="vLLM")upstream++;else ascend++;
    RepositoryModule target=null;
    if(known.TryGetValue(node.Id,out key))foreach(var m in modules)if(m.Id==key&&m.Repository==repo){target=m;break;}
    // Unknown symbols stay explicit instead of inferring an architectural role from a title.
    if(target==null)others.Nodes.Add(node.Id);else target.Nodes.Add(node.Id);
   }
   if(upstream==0||ascend==0)return null;
   foreach(var module in modules)if(module.Nodes.Count>0)map.Modules.Add(module);
   if(others.Nodes.Count>0)map.Modules.Add(others);
   if(map.Modules.Count<6)return null;
   AddJourney(graph,map);
   return map;
  }
  static RepositoryModule Module(string id,string title,string repo,string summary){return new RepositoryModule{Id=id,Title=title,Repository=repo,Summary=summary};}
  static void Bind(Dictionary<string,string> map,string module,params string[] ids){foreach(var id in ids)map[id]=module;}
  public static string SourceRepository(DomainNode node){
   foreach(var url in RepositoryLearning.CodeLocations(node)){
    var path=new Uri(url).AbsolutePath;
    if(path.StartsWith("/vllm-project/vllm/",StringComparison.OrdinalIgnoreCase))return "vLLM";
    if(path.StartsWith("/vllm-project/vllm-ascend/",StringComparison.OrdinalIgnoreCase))return "vLLM-Ascend";
   }return "";
  }
  public static RepositoryModule Parent(RepositoryMap map,string id){return map==null?null:map.Modules.Find(delegate(RepositoryModule m){return m.Nodes.Contains(id);});}
  public static List<RepositoryModule> Children(RepositoryMap map,string parent){return map==null?new List<RepositoryModule>():map.Modules.FindAll(m=>(m.ParentId??"")==(parent??""));}
  public static List<RepositoryModule> Path(RepositoryMap map,string id){
   var path=new List<RepositoryModule>();var seen=new HashSet<string>();
   while(map!=null&&!string.IsNullOrEmpty(id)&&seen.Add(id)){var m=map.Modules.Find(x=>x.Id==id);if(m==null)break;path.Insert(0,m);id=m.ParentId;}return path;
  }
  public static List<string> Members(RepositoryMap map,string id){
   var result=new List<string>();if(map==null)return result;
   foreach(var m in map.Modules)if(Path(map,m.Id).Exists(p=>p.Id==id))result.AddRange(m.Nodes);return result;
  }
  public static string PathLabel(RepositoryMap map,string id){return string.Join(" › ",Path(map,id).ConvertAll(m=>m.Title).ToArray());}
  // Project a source-node relation onto the immediate cards visible at this depth.
  public static string VisibleItem(RepositoryMap map,string scope,string nodeId){
   var owner=Parent(map,nodeId);if(owner==null)return "";if(owner.Id==scope)return nodeId;
   var path=Path(map,owner.Id);if(string.IsNullOrEmpty(scope))return path.Count==0?"":"@architecture/"+path[0].Id;
   int at=path.FindIndex(m=>m.Id==scope);return at>=0&&at+1<path.Count?"@architecture/"+path[at+1].Id:"";
  }
  public static void IncludeUnassigned(DomainGraph graph,RepositoryMap map){
   if(map.Modules==null)return;
   var missing=graph.Nodes.FindAll(delegate(DomainNode n){return !map.Modules.Exists(delegate(RepositoryModule m){return m!=null&&m.Nodes!=null&&m.Nodes.Contains(n.Id);});});if(missing.Count==0)return;
   string id="unassigned";int suffix=1;while(map.Modules.Exists(delegate(RepositoryModule m){return m!=null&&m.Id==id;}))id="unassigned-"+suffix++;
   var bucket=map.Modules.Find(delegate(RepositoryModule m){return m!=null&&m.Title=="待归类节点"&&m.Repository=="补充知识"&&m.Nodes!=null;});
   if(bucket==null){bucket=new RepositoryModule{Id=id,Title="待归类节点",Repository="补充知识",Summary="新补充的节点，尚未指定模块归属。"};map.Modules.Add(bucket);}
   foreach(var node in missing)bucket.Nodes.Add(node.Id);
  }
  static void AddJourney(DomainGraph graph,RepositoryMap map){
   // This route describes the checked-in normal execution path, not a trace or startup order.
   string[] ids={"entry-async-llm","engine-core-step","scheduler-schedule","executor-boundary","worker-npu","ascend-model-runner","engine-core-step","entry-output-loop"};
   foreach(var id in ids){var n=graph.Nodes.Find(delegate(DomainNode x){return x.Id==id;});if(n==null||!(n.Sources??"").Contains(id=="worker-npu"||id=="ascend-model-runner"?AscendRevision:VllmRevision))return;}
   map.Journey.Add(Step(ids[0],"接收请求","前端 / AsyncLLM","prompt 与生成参数","提交给 EngineCore 的请求","AsyncLLM 处理请求并经客户端提交；这不是对 step 的直接调用。"));
   map.Journey.Add(Step(ids[1],"进入引擎循环","跨 IPC → EngineCore","引擎请求与当前状态","本轮调度与执行任务","由引擎循环驱动 step；请求可以跨多个调度轮次。"));
   map.Journey.Add(Step(ids[2],"分配本轮预算","EngineCore / Scheduler","等待与运行中的请求、KV 状态","SchedulerOutput","计算本轮 token 预算并安排缓存；缓存不足等分支可从关联节点继续探索。"));
   map.Journey.Add(Step(ids[3],"派发执行","Executor → Worker 边界","SchedulerOutput","交给 Worker 的执行任务","具体执行器负责调用或 RPC。这里是逻辑边界，不表示所有配置都使用同一种进程拓扑。"));
   map.Journey.Add(Step(ids[4],"进入 NPU Worker","设备 Worker","调度结果及中间张量","交给 NPU Model Runner 的输入","平台配置选定 NPU Worker；Worker 在 execute_model 中委托 model_runner。"));
   map.Journey.Add(Step(ids[5],"运行模型与采样","NPU / Model Runner","本轮输入与缓存状态","执行结果，必要时另行采样","Runner 组织输入、前向和设备实现。采样可能由单独的 sample_tokens 完成，不能等同于 execute_model 总是直接返回 token。"));
   map.Journey.Add(Step(ids[6],"更新请求状态","Worker → EngineCore","模型执行 / 采样结果","EngineCoreOutputs 与下一轮状态","step 等待执行结果，必要时触发采样，再通过 update_from_output 更新调度状态。"));
   map.Journey.Add(Step(ids[7],"回传生成结果","跨 IPC → 前端","EngineCore 输出","调用者收到的增量结果","输出协程拉取并处理结果；未完成的请求继续参与引擎循环。"));
  }
  static RepositoryStep Step(string id,string title,string boundary,string input,string output,string summary){return new RepositoryStep{NodeId=id,Title=title,Boundary=boundary,Input=input,Output=output,Summary=summary};}
  public static string Validate(DomainGraph graph,RepositoryMap map){
   if(map.Modules==null||map.Modules.Count==0||map.Modules.Count>60||map.Journey==null||map.Journey.Count>100)return "架构模块或旅程数量无效。";
   var modules=new HashSet<string>();var members=new HashSet<string>();
   foreach(var m in map.Modules){
    if(m==null||string.IsNullOrWhiteSpace(m.Id)||m.Id.Length>128||!modules.Add(m.Id)||string.IsNullOrWhiteSpace(m.Title)||m.Title.Length>120||string.IsNullOrWhiteSpace(m.Repository)||m.Repository.Length>120||(m.Summary??"").Length>40000||m.Nodes==null)return "架构模块缺少有效名称、仓库或成员列表。";
    foreach(var id in m.Nodes)if(!members.Add(id)||!graph.Nodes.Exists(delegate(DomainNode n){return n.Id==id;}))return "架构节点重复归属或不存在。";
   }
   foreach(var m in map.Modules){
    var seen=new HashSet<string>{m.Id};var current=m;
    while(!string.IsNullOrEmpty(current.ParentId)){
     var parent=map.Modules.Find(x=>x.Id==current.ParentId);if(parent==null)return "父模块不存在。";
     if(!seen.Add(parent.Id))return "模块层级不能循环或指向自身。";
     if(parent.Repository!=m.Repository)return "子模块与父模块必须属于同一仓库。";
     if(seen.Count>12)return "模块层级最多 12 层，请合并过细的层级。";current=parent;
    }
   }
   if(members.Count!=graph.Nodes.Count)return "架构需包含全部节点，未分类内容可放入补充模块。";
   foreach(var step in map.Journey)if(step==null||string.IsNullOrWhiteSpace(step.Title)||!members.Contains(step.NodeId))return "请求旅程引用了不存在的节点。";
   return "";
  }
 }
}
