using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Threading;

#pragma warning disable 0649
namespace ElymsPet {
 public sealed class DshWorkbench : IDisposable {
  readonly PetState state;
  readonly WorkScope work;
  readonly DispatcherTimer timer;
  readonly Func<DshKnowledgeManifest> prepareKnowledge;
  readonly List<DshJob> history=new List<DshJob>();
  bool polling;
  public DshJob Current;
  public string LastConnection="";
  public event Action Changed;
  public event Action<DshJob> Finished;
  public event Action<bool,string> ConnectionChecked;
  public DshWorkbench(PetState state,IWorkScheduler scheduler,bool preview,Func<DshKnowledgeManifest> prepareKnowledge=null) {
   this.state=state;
   this.prepareKnowledge=prepareKnowledge;
   work=new WorkScope(scheduler,delegate(string error) {polling=false;timer.Stop();SetImmediateError(error);},!preview);
   timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1.5)};
   timer.Tick+=delegate {Poll();};
  }
  public DshJob[] History {get {return history.ToArray();}}
  public bool Busy {get {return Current!=null&&Current.Busy;}}
  public string HandshakePath {get {return DshApi.HandshakePath;}}
  public bool CanRun {get {string error;return PluginAccess.Enabled(state,"dsh")&&state.DshEnabled&&DshApi.ReadHandshake(out error)!=null;}}
  public string StatusLine {
   get {
    if(!PluginAccess.Enabled(state,"dsh"))return "DSH 插件未安装或未启用。";
    if(!state.DshEnabled) return "○ DSH 委托已关闭";
    string error;var handshake=DshApi.ReadHandshake(out error);
    if(handshake==null) return "○ DSH 桥接未运行（打开 DSH 后可用）";
    return "检测到 DSH 桥接 · 工作区 "+handshake.workspace;
   }
  }
  public string PromptContext() {if(!PluginAccess.Enabled(state,"dsh"))return "DSH 插件不可用，不可委托任务。";return DshApi.PromptContext(state)+(prepareKnowledge==null?"":!PluginAccess.Enabled(state,"graph")?" 委托附带现有共享知识文件路径；图谱插件未启用，不能提交自动图谱编辑。":" 委托自动附带当前存档的流程、知识图谱和资料索引的真实路径。编辑图谱时按 GRAPH_EDIT.md 提交修改并核对结果；草稿不得直接执行。");}
  public bool Start(string task,string permission,string title) {
   if(!PluginAccess.Enabled(state,"dsh")){SetImmediateError("请先安装并启用 DSH 插件。");return false;}
   if(work.IsDisposed||Busy) return false;
   task=(task??"").Trim();
   if(task.Length==0) return false;
   if(!state.DshEnabled) {SetImmediateError("DSH 委托已在设置里关闭。");return false;}
   string error;var handshake=DshApi.ReadHandshake(out error);
   if(handshake==null) {SetImmediateError("DSH 桥接没有运行："+error);return false;}
   DshKnowledgeManifest knowledge;
   try {knowledge=prepareKnowledge==null?null:prepareKnowledge();}
   catch(Exception e){SetImmediateError("准备存档资料失败，任务未提交："+e.Message);return false;}
   Current=new DshJob {Status="starting",Task=task,Title=title??"",Permission=permission??"workspace-write",Cwd=handshake.workspace,UpdatedAt=DateTime.Now};
   Raise();
   work.Run(delegate {
    return DshApi.Start(task,permission,DshApi.Clip(title,80),knowledge);
   },delegate(DshJob job) {
    Current=job;
    if(job==null) Current=new DshJob {Status="error",Error="DSH 返回了空结果",Task=task};
    Raise();
    if(Current.Busy) timer.Start(); else Finish(Current);
   });
   return true;
  }
  public void Cancel() {
   if(Current==null||!Current.Busy||Current.Id.Length==0) return;
   string id=Current.Id;
   work.Run(delegate {return DshApi.Cancel(id);},delegate(bool ok) {
    if(ok&&Current!=null&&Current.Id==id) {Current.Status="cancelling";Current.UpdatedAt=DateTime.Now;Raise();}
   },delegate(string error) {LastConnection=error;Raise();});
  }
  public void TestConnection() {
   if(!PluginAccess.Enabled(state,"dsh")){SetImmediateError("请先安装并启用 DSH 插件。");return;}
   work.Run(delegate {return DshApi.Health();},delegate(DshHealthResult result) {
    LastConnection=result.Ok?"● DSH 桥接正常 · 工作区 "+result.Workspace:("○ "+result.Error);
    Raise();
    if(ConnectionChecked!=null)ConnectionChecked(result.Ok,LastConnection);
   },delegate(string error) {LastConnection=error;Raise();if(ConnectionChecked!=null)ConnectionChecked(false,error);});
  }
  public void OpenWeb() {
   if(!PluginAccess.Enabled(state,"dsh")){SetImmediateError("请先安装并启用 DSH 插件。");return;}
   work.Run(delegate{return DshApi.BrowserUrl();},delegate(string url){
    try {System.Diagnostics.Process.Start(url);LastConnection="已打开 DSH。";}
    catch {LastConnection="浏览器未能启动，请检查默认浏览器。";}
    Raise();if(ConnectionChecked!=null)ConnectionChecked(true,LastConnection);
   },delegate(string error){LastConnection=error;Raise();if(ConnectionChecked!=null)ConnectionChecked(false,error);});
  }
  void Poll() {
   if(polling||Current==null||!Current.Busy||Current.Id.Length==0) {if(Current==null||!Current.Busy)timer.Stop();return;}
   polling=true;
   string id=Current.Id;
   work.Run(delegate {return DshApi.Poll(id);},delegate(DshJob job) {
    polling=false;
    if(Current==null||Current.Id!=id) return;
    DshJob previous=Current;
    Current=job;
    if(Current.Id.Length==0) {
     Current.Id=id;Current.Task=previous.Task;Current.Permission=previous.Permission;Current.Cwd=previous.Cwd;Current.Title=previous.Title;
    }
    Raise();
    if(Current.Busy) timer.Start(); else Finish(Current);
   });
  }
  void Finish(DshJob job) {
   timer.Stop();
   if(job==null) return;
   job.UpdatedAt=DateTime.Now;
   bool exists=false;
   for(int i=0;i<history.Count;i++)if(history[i].Id==job.Id)exists=true;
   if(!exists&&job.Id.Length>0) {history.Add(job);while(history.Count>12)history.RemoveAt(0);}
   Raise();
   if(Finished!=null)Finished(job);
  }
  void SetImmediateError(string error) {
   Current=new DshJob {Status="error",Error=error,UpdatedAt=DateTime.Now};
   Raise();
   if(Finished!=null)Finished(Current);
  }
  void Raise() {if(Changed!=null)Changed();}
  public void Dispose() {work.Dispose();timer.Stop();polling=false;}
 }
}
