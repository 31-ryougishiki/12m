using System;
using System.IO;
using System.Collections.Generic;
namespace ElymsPet {
 static class PluginTests {
  static void Check(bool ok,string label){if(!ok)throw new Exception("Plugins: "+label);Console.WriteLine("PASS plugins: "+label);}
  public static void Run(string root){
   string folder=Path.Combine(root,"plugin-lifecycle"),app=AppDomain.CurrentDomain.BaseDirectory;Directory.CreateDirectory(folder);
   string data=Path.Combine(folder,"archives.json");File.WriteAllText(data,"preserve workflow and graph data");
   var manager=new PluginManager(folder,app,false);
   Check(!Array.Exists(typeof(PluginManager).Assembly.GetReferencedAssemblies(),x=>x.Name.StartsWith("ElymsPlugin.")),"host has no static dependency on feature assemblies");
   Check(manager.Catalog.Count==5&&!manager.Installed("archive")&&!manager.Enabled("archive"),"new installation contains no active optional features");
   Check(!manager.Change("../../outside","install"),"unknown/path traversal package is rejected");
   Check(manager.Change("archive","install")&&manager.Installed("archive")&&!manager.Enabled("archive")&&manager.PendingRestart,"install is staged and does not load code until restart");
   manager=new PluginManager(folder,app,false);Check(manager.Enabled("archive")&&!manager.Enabled("graph"),"only explicitly installed feature activates after restart");
   Check(manager.Change("archive","disable")&&manager.Enabled("archive"),"disable leaves in-flight runtime unchanged until restart");
   manager=new PluginManager(folder,app,false);Check(!manager.Enabled("archive")&&manager.Installed("archive"),"disabled package stays installed");
   Check(manager.Change("archive","enable"),"disabled plugin can be enabled without reinstall");
   manager=new PluginManager(folder,app,false);Check(manager.Enabled("archive"),"reenabled plugin returns after restart");
   string installed=Path.Combine(folder,"plugins","installed","archive",manager.Package("archive").Hash,"module.dll");
   Check(manager.Change("archive","remove")&&File.Exists(installed),"uninstall is staged while existing code may still be in use");
   manager=new PluginManager(folder,app,false);Check(!manager.Installed("archive")&&!manager.Enabled("archive")&&!File.Exists(installed),"next startup removes uninstalled code");
   Check(File.ReadAllText(data)=="preserve workflow and graph data","uninstall preserves user data");
   Check(manager.Change("archive","install"),"reinstall from bundled package succeeds");manager=new PluginManager(folder,app,false);
   Check(manager.Enabled("archive")&&File.Exists(data),"reinstall retains existing data");
   File.WriteAllText(installed,"damaged");manager=new PluginManager(folder,app,false);
   Check(!manager.Enabled("archive")&&manager.StateText("archive").Contains("不匹配"),"corrupt installed code is rejected without preventing host startup");
   Check(manager.Change("archive","install"),"repair replaces corrupt package");manager=new PluginManager(folder,app,false);Check(manager.Enabled("archive"),"repair is verified on restart");
   var missing=new PluginManager(Path.Combine(root,"plugin-missing"),Path.Combine(root,"empty-app"),false);
   Check(!missing.Change("bili","install")&&!missing.Installed("bili"),"missing installer cannot leave an installed registry entry");
   var state=new PetState{Plugins=missing,AiChat=true,DshEnabled=true};
   Check(!PluginAccess.Page(state,PanelTab.Graph)&&PluginAccess.Page(state,PanelTab.Companion),"optional routes require a plugin while companion stays available");
   var scheduler=new NoJobs();var feedback=new PetFeedback(delegate{},delegate{},delegate{});
   using(var archive=new ArchiveService(state,scheduler,feedback,new TransientRepository<ArchiveBook>(ArchiveStore.Demo()),true)){
    int turns=archive.Slot.Turns.Count;archive.Focus("should not start");archive.AskNext();Check(!archive.Answer("should not save")&&archive.Slot.Turns.Count==turns,"uninstalled interview cannot start through non-page entry points");
    archive.UploadCloud(true);archive.ListCloud(delegate(List<ArchiveCloudVersion> versions){throw new Exception("unexpected cloud callback");});
    string message;Check(!archive.ApplyGraphEdit(archive.Slot,new GraphEditRequest(),out message)&&message.Contains("插件"),"uninstalled graph rejects external editing");
    Check(!archive.SaveDomainGraph(new DomainGraph{Id="x",Title="x"}),"manual graph mutation respects availability");
   }
   using(var bili=new BiliService(state,scheduler,delegate{return "";},true)){Check(!bili.Browse("hello",true),"uninstalled Bili cannot browse via conversation or autonomy");bili.SendComment(null,"x",delegate(BiliCommentResult result){Check(!string.IsNullOrEmpty(result.Error),"Bili comment is rejected before any network work");});}
   using(var dsh=new DshWorkbench(state,scheduler,true)){Check(!dsh.CanRun&&!dsh.Start("no task","workspace-write","test"),"uninstalled DSH cannot submit jobs");dsh.TestConnection();dsh.OpenWeb();}
   Check(scheduler.Queued==0,"disabled features queue no network or external work");
  }
  sealed class NoJobs:IWorkScheduler {public int Queued;public void Queue(Action a){Queued++;}public void Post(Action a){a();}}
 }
}
