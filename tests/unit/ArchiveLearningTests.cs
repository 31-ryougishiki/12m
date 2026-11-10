using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
namespace ElymsPet {
 static class ArchiveLearningTests {
  static int checks;
  static void Check(bool pass,string name) {if(!pass)throw new Exception("Archive learning: "+name);checks++;Console.WriteLine("PASS archive learning: "+name);}
  static ArchiveWorkflow Workflow(string name,string trigger) {return new ArchiveWorkflow{Title=name,Trigger=trigger,Preconditions="条件已检查",Steps="1. 执行\n2. 核验",Checks="结果符合预期",Exceptions="失败时停止并恢复",Confirmed=true};}
  public static void Run(string root) {
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"archive-learning");
   try {
    var book=ArchiveStore.Normalize(new ArchiveBook());var slot=ArchiveStore.Active(book);
    ArchiveStore.Merge(slot,new[]{new ArchiveItem{Domain="工程",Title="模型部署",Level="熟练",Evidence="我能够独立完成部署"}});
    string id=slot.Items[0].Id;DateTime at=slot.Items[0].At;
    ArchiveStore.Merge(slot,new[]{new ArchiveItem{Domain="工程",Title="模型部署",Level="熟练",Evidence="我能够独立完成部署"}});
    Check(slot.Changes.Count==1&&slot.Items[0].At==at,"identical extraction is a no-op");
    ArchiveStore.Merge(slot,new[]{new ArchiveItem{Domain="工程",Title="模型部署",Level="了解"}});
    Check(slot.Items[0].Level=="熟练"&&slot.Changes.Count==1,"empty evidence cannot downgrade existing knowledge");
    ArchiveStore.Merge(slot,new[]{new ArchiveItem{Domain="工程",Title="模型部署",Level="了解",Boundary="尚未掌握",Evidence="纠正：还不能独立部署"}});
    Check(slot.Items[0].Id==id&&slot.Changes.Count==2&&slot.Changes[1].Before.Level=="熟练","update keeps stable id and independent before snapshot");
    var workflow=Workflow("模型部署","发布模型版本");slot.Workflows.Add(workflow);slot.Workflows.Add(Workflow("报销审批","提交报销"));
    var draft=Workflow("模型部署草稿","部署模型");draft.Confirmed=false;slot.Workflows.Add(draft);
    string quote="我排查模型部署故障时，发现健康检查地址配置错了，改好后恢复。";
    ArchiveStore.AddTurn(slot,"answer",quote);
    var result=ArchiveInterview.Parse("{\"episodes\":[{\"task\":\"模型部署故障\",\"workflow\":\"模型部署\",\"situation\":\"健康检查失败\",\"decision\":\"检查地址配置\",\"outcome\":\"成功\",\"lesson\":\"发布前检查地址\",\"evidence\":\""+quote+"\"},{\"task\":\"伪造成功\",\"outcome\":\"成功\",\"evidence\":\"用户从未说过的经历\"}]}");
    ArchiveKnowledge.Merge(slot,result);ArchiveKnowledge.Merge(slot,result);
    Check(slot.Episodes.Count==1&&slot.Episodes[0].At==slot.Turns[0].At,"only grounded user episodes accepted, duplicate extraction ignored");
    slot.Resources.Add(new ArchiveResource{Title="模型部署文档",Kind="文档",Url="https://example.org/deploy",Content="健康检查端点配置说明"});
    string recall=ArchiveRecall.Build(slot,"帮我发布模型",14000);
    Check(recall.Contains("模型部署")&&recall.Contains("失败时停止并恢复")&&!recall.Contains("报销审批")&&!recall.Contains("模型部署草稿"),"task retrieval includes complete relevant workflow only");
    Check(recall.Contains("模型部署故障")&&recall.Contains("https://example.org/deploy"),"relevant episodes and source attribution recalled");
    Check(!ArchiveRecall.Build(slot,"今天月亮漂亮",14000).Contains("模型部署"),"unrelated chat does not receive work skills");
    string small=ArchiveRecall.Build(slot,"发布模型",100);
    Check(small.Length<=100&&!small.Contains("已确认流程「"),"oversized workflows omitted atomically within budget");
    Check(!ArchiveLearning.Feedback(slot,workflow,"成功","  "),"empty feedback rejected");
    Check(ArchiveLearning.Feedback(slot,workflow,"失败","新环境缺少运行依赖")&&!workflow.Confirmed,"failure feedback invalidates workflow confirmation");
    Check(!ArchiveRecall.Build(slot,"发布模型",14000).Contains("已确认流程「模型部署」"),"failed workflow excluded from active skill recall");
    ArchiveLearning.Feedback(slot,workflow,"成功","补齐依赖后重试成功");
    Check(!workflow.Confirmed,"success feedback never auto-confirms revised skills");
    ArchiveKnowledge.WriteFiles(slot);string folder=ArchiveKnowledge.Folder(slot);
    Check(File.ReadAllText(Path.Combine(folder,"episodes.md")).Contains("新环境缺少运行依赖")&&File.ReadAllText(Path.Combine(folder,"changes.md")).Contains("UPDATE"),"feedback and history exported locally");
    bool saved=ArchiveStore.Save(book);Check(saved,"learning state saved: "+ArchiveStore.LastError);slot=ArchiveStore.Active(ArchiveStore.Load());
    Check(slot.Episodes.Count==3&&slot.Changes.Count==2&&slot.Items[0].Id==id,"history and episodes survive restart");
    var feedback=new PetFeedback(delegate {},delegate {},delegate {});
    using(var service=new ArchiveService(new PetState(),new NoNetwork(),feedback,new TransientRepository<ArchiveBook>(ArchiveStore.Load()),true)) {
     var change=service.Slot.Changes[1];Check(service.RestoreChange(change)&&service.Slot.Items[0].Level=="熟练","user can restore the current update");
     Check(!service.RestoreChange(change),"stale restoration cannot overwrite newer state");
     service.DeleteItem(service.Slot.Items[0]);Check(service.Slot.Changes.Count==0,"forgetting a knowledge item removes its history too");
    }
    using(var services=new PetServices(new PetState(),new NoNetwork(),feedback,delegate{return false;},root,true,new SystemClock())) {
     var active=services.Archive.Slot;active.Workflows.Add(Workflow("模型部署","发布模型"));active.Workflows.Add(Workflow("报销审批","报销"));
     var build=typeof(ChatService).GetMethod("BuildTurns",BindingFlags.Instance|BindingFlags.NonPublic);
     var turns=(ChatTurn[])build.Invoke(services.Conversation,new object[]{"发布模型","",""});
     Check(turns[0].Content.Contains("已确认流程「模型部署」")&&!turns[0].Content.Contains("报销审批"),"production chat pipeline uses task-conditioned recall");
    }
    Check(ArchiveInterview.BuildSystem(slot).Contains("episodes")&&ArchiveInterview.BuildExtract(slot)[0].Content.Contains("episodes"),"both extraction paths include experience schema");
    Console.WriteLine("PASS archive learning: "+checks+" checks");
   } finally {Storage.Folder=previous;}
  }
  sealed class NoNetwork:IWorkScheduler {public void Queue(Action action){throw new Exception("Unexpected network request");} public void Post(Action action){action();}}
 }
}
