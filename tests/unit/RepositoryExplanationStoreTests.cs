using System;
using System.IO;
namespace ElymsPet {
 static class RepositoryExplanationStoreTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Explanation store: "+name);Console.WriteLine("PASS explanation store: "+name);}
  public static RepositoryExplanationLog Record(string slot="slot",string topic="topic",string node="node",string text="讲解正文\n\n流程答疑：中文",int day=1){return new RepositoryExplanationLog{SlotId=slot,TopicId=topic,NodeId=node,Focus=RepositoryLearning.ExplanationFocus,Output=text,DisplayText="AI 讲解 · 待核实\n"+text,Ok=true,ReviewStatus="complete",ObjectReviewStatus="complete",FlowReviewStatus="complete",CompletedAt="2026-11-"+day.ToString("00")+"T10:00:00+08:00",SourceRevision="source",PromptRevision=RepositoryExplanationStore.PromptRevision,ObjectReviewedOutput="讲解正文",FlowAppendix="\n\n流程答疑：中文"};}
  sealed class NoWork : IWorkScheduler {public void Queue(Action action){throw new Exception("Opening a saved explanation must not start work");}public void Post(Action action){action();}}
  public static void Run(){
   string root=Path.Combine(Path.GetTempPath(),"elyms-explanation-store-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try{
    string error;var store=new RepositoryExplanationStore(root);var first=Record();
    Check(store.Save(first,"",out error),"complete result saves: "+error);
    var loaded=new RepositoryExplanationStore(root).Load("slot","topic","node",out error);
    Check(loaded.Output==first.Output&&loaded.ObjectReviewedOutput==first.ObjectReviewedOutput&&loaded.FlowAppendix==first.FlowAppendix,"new instance restores exact body, appendix and review records");
    Check(store.Load("slot-other","topic","node",out error)==null&&store.Load("slot","topic-other","node",out error)==null&&store.Load("slot","topic","node-other",out error)==null,"same node names in different slots or topics stay isolated");
    Check(store.PathFor("../escape","../topic","../node").StartsWith(Path.Combine(root,"learning","explanations"))&&!store.PathFor("../escape","../topic","../node").Contains(".."),"untrusted ids cannot become filesystem paths");
    foreach(string failure in new[]{"error","truncated","clipped","review","feedback"}){
     var bad=Record(text:"不可覆盖",day:2);if(failure=="error")bad.Ok=false;if(failure=="truncated")bad.Truncated=true;if(failure=="clipped")bad.ResponseClipped=true;if(failure=="review")bad.ReviewStatus="failed";if(failure=="feedback")bad.Focus="对我的复述指出正确处";
     Check(!store.Save(bad,"",out error)&&store.Load("slot","topic","node",out error).Output==first.Output,"failed or unrelated generation cannot replace saved result: "+failure);
    }
    var second=Record(text:"新正文 secret-value",day:2);Check(store.Save(second,"secret-value",out error),"regeneration saves atomically");
    string path=store.PathFor("slot","topic","node");Check(File.Exists(path+".bak")&&!File.ReadAllText(path).Contains("secret-value"),"previous result backed up and credentials redacted");
    File.WriteAllText(path,"broken JSON");Check(new RepositoryExplanationStore(root).Load("slot","topic","node",out error).Output==first.Output,"corrupt main file recovers previous complete backup");
    string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"file");Check(!new RepositoryExplanationStore(blocked).Save(first,"",out error)&&error.Length>0,"write failure is surfaced");
    string logRoot=Path.Combine(root,"legacy"),logDir=Path.Combine(logRoot,"repository-explanations");Directory.CreateDirectory(logDir);
    var old=Record(day:1);old.SourceRevision=old.PromptRevision=null;var newer=Record(text:"从旧日志找回",day:2);newer.SourceRevision=newer.PromptRevision=null;var broken=Record(text:"新失败不覆盖",day:3);broken.ReviewStatus="failed";var feedback=Record(text:"复述反馈不混入",day:4);feedback.Focus="对我的复述指出正确处";
    File.WriteAllLines(Path.Combine(logDir,"20261101.jsonl"),new[]{GraphEditing.Json(newer),"invalid",GraphEditing.Json(old),GraphEditing.Json(broken),GraphEditing.Json(feedback)});
    var legacyStore=new RepositoryExplanationStore(logRoot);loaded=legacyStore.Load("slot","topic","node",out error);
    Check(loaded.Output==newer.Output&&File.Exists(legacyStore.PathFor("slot","topic","node")),"legacy logs import latest successful explanation despite malformed, newer failed and feedback lines");
    Check(RepositoryExplanationStore.Notice(loaded,"current").Contains("版本未记录")&&RepositoryExplanationStore.Notice(loaded,"current").Contains("规则已更新"),"legacy result retains unknown provenance instead of claiming current version");
    Check(!RepositoryExplanationStore.Notice(first,"source").Contains("已变化")&&RepositoryExplanationStore.Notice(first,"changed").Contains("资料已变化"),"stale graph is labeled without dropping readable result");
    // A completed log may be newer than latest.json after an interrupted save.
    var newest=Record(text:"崩溃间隙的完整结果",day:5);File.AppendAllText(Path.Combine(logDir,"20261101.jsonl"),GraphEditing.Json(newest)+Environment.NewLine);
    Check(new RepositoryExplanationStore(logRoot).Load("slot","topic","node",out error).Output==newest.Output,"restart recovers completed log newer than saved copy");
    var isolated=new RepositoryExplanationStore(null);
    using(var service=new ArchiveService(new PetState(),new NoWork(),null,new TransientRepository<ArchiveBook>(new ArchiveBook()),true,null,null,isolated)){
     var graph=RepositoryLearningExample.Create();var node=graph.Nodes[0];var saved=Record(service.Slot.Id,graph.Id,node.Id);saved.SourceRevision=GraphEditing.Revision(graph);isolated.Save(saved,"",out error);
     string notice;Check(service.SavedRepositoryExplanation(graph,node,out notice).Output==saved.Output&&!notice.Contains("已变化"),"service opens saved explanation without key, network or queued work");
     graph.Nodes[0].Code+="\nchanged";Check(service.SavedRepositoryExplanation(graph,node,out notice)!=null&&notice.Contains("已变化"),"service keeps old explanation available after code change");
    }
   }finally{Directory.Delete(root,true);}
  }
 }
}
