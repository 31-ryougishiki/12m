using System;
namespace ElymsPet {
 static class GraphReadingTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Graph reading: "+name);Console.WriteLine("PASS graph reading: "+name);}
  public static void Run(string root){
   using(var h=new GraphAiCreationTests.Harness(true,root)){
    var view=new GraphReadingView{SlotId=h.Service.Slot.Id,TopicId=h.Graph.Id,NodeId="worker-npu",Mode="module",ModuleId="npu",Fullscreen=true,DetailsVisible=true,Search="Worker"};view.Visible.Add("worker-npu");h.Service.ReadingView=()=>view;
    var reading=h.Service.CaptureGraphReading("这里的作用是什么");Check(reading.HasView&&reading.Text.Contains("全屏图谱 / 模块内部")&&reading.NodeId=="worker-npu"&&reading.ModuleId=="npu"&&reading.Text.Contains("搜索条件：Worker"),"capture carries the actual UI mode, selection, module and filter");
    Check(reading.CodeUrls.Count>0&&reading.Text.Contains("节点索引")&&reading.Text.Contains("架构范围")&&reading.Text.Contains("worker-npu →"),"focused source links, full architecture index and relationships are supplied");
    string captured=reading.Text;view.NodeId="scheduler-schedule";view.ModuleId="cache";view.Visible.Clear();Check(reading.Text==captured&&reading.NodeId=="worker-npu"&&reading.ModuleId=="npu","later UI navigation cannot mutate an in-flight question");
    h.SetAddResponse("snapshot-added");GraphEditOutcome result=null;h.Service.EditGraphWithAi("在这个模块加一个节点",reading,r=>result=r);h.Queue.Run();Check(result.Success&&RepositoryArchitecture.Parent(h.Graph.Architecture,"snapshot-added").Id=="npu"&&!h.Api.Prompt.Contains("本轮本地源码读取结果")&&h.Api.PlanCalls==1,"chat edits stay scoped to the captured module and run DSH source triage without host excerpts");
    h.Service.EditGraphWithAi("继续修改",reading,r=>result=r);Check(!result.Success&&result.Message.Contains("已变化"),"stale chat edits are rejected before the second API request");
    view.NodeId="";view.ModuleId="npu";view.Mode="overview";var module=h.Service.CaptureGraphReading("");Check(module.NodeId==""&&module.Text.Contains("模块入口示例")&&module.CodeUrls.Count>0,"module overview gets bounded representative code without inventing a selected node");
    view.ModuleId="";view.Mode="journey";view.JourneyIndex=6;view.NodeId=h.Graph.Architecture.Journey[6].NodeId;var journey=h.Service.CaptureGraphReading("");Check(journey.Text.Contains("当前旅程第 7 步")&&journey.Text.Contains(h.Graph.Architecture.Journey[6].Output),"repeated engine visits keep their distinct journey stage and IO");
    view.SlotId="another-slot";h.Service.CurrentDomainId=h.Graph.Id;var hidden=h.Service.CaptureGraphReading("");Check(!hidden.HasView&&hidden.NodeId==""&&hidden.Text.Contains("没有可见"),"old-profile UI cannot supply a false selection");
    h.Service.ReadingView=null;var named=h.Service.CaptureGraphReading("解释 worker-npu");Check(!named.HasView&&named.CodeUrls.Count>0,"named-node questions retrieve sources even without a graph window");
    var symbol=h.Service.CaptureGraphReading("Scheduler 怎样分配 token");Check(symbol.Text.Contains("NodeId=scheduler-schedule")&&symbol.CodeUrls.Count>0,"common class name retrieves details without requiring the full display title");
    var turns=new[]{new ChatTurn("system",named.Text),new ChatTurn("user","解释")};named.AttachSources(turns);Check(turns[0].Content.Contains("尚未配置")&&turns[1].Content=="解释","missing local mapping remains explicit in the outgoing API context");
    named.Complete(new[]{new ChatTurn("system",named.Text),new ChatTurn("user","解释")},input=>{Check(input[input.Length-1].Content.Contains("[[DSH_READ]]")&&!string.Join("",Array.ConvertAll(input,t=>t.Content)).Contains("本轮本地源码读取结果"),"graph Q&A advertises DSH research without attaching host-read source excerpts");return new ChatOutcome{Ok=true,Text="回答"};});
   }
  }
 }
}
