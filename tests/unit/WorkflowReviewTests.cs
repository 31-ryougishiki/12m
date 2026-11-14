using System;
using System.Collections.Generic;
using System.IO;
namespace ElymsPet {
 static class WorkflowReviewTests {
  static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS workflow review: "+name);}
  sealed class Scheduler:IWorkScheduler{public Queue<Action> Jobs=new Queue<Action>();public void Queue(Action a){Jobs.Enqueue(a);}public void Post(Action a){a();}public void Run(){while(Jobs.Count>0)Jobs.Dequeue()();}}
  sealed class Api:IArchiveApi{public Queue<ChatOutcome> Replies=new Queue<ChatOutcome>();public ChatTurn[] Last;public int Calls;public ChatOutcome Complete(string key,string url,string model,bool thinking,ChatTurn[] turns,int timeout,int tokens,double temperature){Last=turns;Calls++;return Replies.Count>0?Replies.Dequeue():new ChatOutcome{Ok=false,Status=401,Error="test failure"};}public WebOutcome Search(string a,string b,string c){throw new Exception("No web needed");}public WebOutcome Fetch(string a,string b,string c){throw new Exception("No web needed");}public void Reply(string text){Replies.Enqueue(new ChatOutcome{Ok=true,Text=text});}}
  public static void Run(string root){
   string prior=Storage.Folder;Storage.Folder=Path.Combine(root,"review-files");ArchiveService service=null;
   try{
    var book=ArchiveStore.Normalize(new ArchiveBook());var slot=book.Slots[0];var w=new ArchiveWorkflow{Title="模型部署与测试",Trigger="升级后验证",Preconditions="8卡机器",Steps="1. 启动服务\n```sh\n1. literal code\nserve --port 8000\n```\n2. 执行测试",Checks="返回200",Exceptions="检查日志",Confirmed=true};slot.Workflows.Add(w);slot.Workflows.Add(new ArchiveWorkflow{Title="模型性能分析",Steps="采集数据"});
    Check(WorkflowReview.SplitSteps(w.Steps).Count==2,"step splitter preserves fenced multiline commands");
    var repo=new JsonRepository<ArchiveBook>(Path.Combine(root,"workflow-review.json"));repo.Save(book);var state=new PetState{AiChat=true,ApiKey="sk-test-review-key"};var scheduler=new Scheduler();var api=new Api();var feedback=new PetFeedback(delegate{},delegate{},delegate{});
    service=new ArchiveService(state,scheduler,feedback,repo,false,api);w=service.Slot.Workflows[0];
    List<ArchiveWorkflow> found=null;api.Reply("{\"matches\":[0,0,999,1]}");service.MatchWorkflow("拉起模型跑测",delegate(List<ArchiveWorkflow> values){found=values;});scheduler.Run();Check(found.Count==2&&found[0]==w,"semantic match uses only existing candidates and removes duplicate/out-of-range indices");
    api.Reply("{\"matches\":[]}");service.MatchWorkflow("烘焙面包",delegate(List<ArchiveWorkflow> values){found=values;});scheduler.Run();Check(found.Count==0&&!service.ReviewingWorkflow,"unmatched name does not invent workflow");
    int calls=api.Calls;service.MatchWorkflow(w.Title,delegate(List<ArchiveWorkflow> values){found=values;});Check(found.Count==1&&api.Calls==calls,"exact title matches without API");
    Check(service.StartWorkflowReview(w)&&service.Slot.Review.Units.Count==6,"review traverses conditions, individual steps, checks and exceptions");
    service.SkipWorkflowUnit();service.SkipWorkflowUnit();Check(service.Slot.Review.Position==2&&w.Confirmed,"skipping unchanged units preserves confirmation");
    string originalSteps=w.Steps;service.Answer("端口换成8001");Check(repo.Load().Slots[0].Review.PendingAnswer=="端口换成8001","answer persisted before network");
    scheduler.Run();Check(service.ReviewWaiting&&w.Steps==originalSteps&&service.Slot.Review.Position==2,"failure keeps original workflow and current position");
    service.Dispose();service=new ArchiveService(state,scheduler,feedback,repo,false,api);w=service.Slot.Workflows[0];int turns=service.Slot.Turns.Count;
    api.Reply("{\"updated\":\"1. 启动服务\\n```sh\\n1. literal code\\nserve --port 8001\\n```\"}");service.ResumeInterview();scheduler.Run();
    Check(w.Steps.Contains("--port 8001")&&w.Steps.EndsWith("2. 执行测试")&&w.Checks=="返回200"&&w.Preconditions=="8卡机器","update modifies only current step and preserves other fields");
    Check(!w.Confirmed&&service.Slot.Review.Position==3&&service.Slot.Turns.Count==turns+1,"successful retry advances once, does not duplicate answer and invalidates confirmation");
    var synced=ArchiveCloudCodec.Decode(ArchiveCloudCodec.Encode(service.Book));Check(synced.Slots[0].Review.Position==3,"cloud snapshot includes review progress");
    service.SkipWorkflowUnit();service.SkipWorkflowUnit();service.SkipWorkflowUnit();Check(!service.ReviewingWorkflow&&service.Slot.Paused,"review completes without wandering into ordinary interview");
    service.StartWorkflowReview(w);w.Checks="外部修改";int before=api.Calls;service.Answer("补充用途");Check(service.ReviewWaiting&&api.Calls==before,"external workflow change blocks stale session update");service.EndWorkflowReview();
    service.StartWorkflowReview(w);api.Reply("{\"updated\":\""+new string('x',501)+"\"}");string old=w.Trigger;service.Answer("补充");scheduler.Run();Check(w.Trigger==old&&service.ReviewWaiting,"oversized field is rejected without clipping stored workflow");
   }finally{if(service!=null)service.Dispose();Storage.Folder=prior;}
  }
 }
}
