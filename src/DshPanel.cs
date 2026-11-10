using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace ElymsPet {
 /// <summary>「DSH 工作台」页：手动提交任务、看进度和结果；聊天里的主动提案也可以落到这里。</summary>
 public sealed class DshPanel {
  readonly PetWindow pet;
  readonly DshWorkbench dsh;
  TextBlock status,connection,currentTitle,currentMeta,resultLabel;
  TextBox task,result;
  CheckBox fullAccess;
  Button start,cancel,test,open;
  StackPanel historyList;
  public UIElement Root {get; private set;}
  public DshPanel(PetWindow target) {
   pet=target;dsh=pet.Dsh;
   Root=Build();
   dsh.Changed+=Refresh;
   dsh.ConnectionChecked+=OnConnection;
   dsh.Finished+=OnFinished;
   Refresh();
  }
  UIElement Build() {
   status=Ui.Text("",12,"#447969");
   connection=Ui.Text("",11,"#795F69");
   task=Ui.Input("",86);task.MaxLength=8000;task.AcceptsReturn=true;task.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;task.VerticalContentAlignment=VerticalAlignment.Top;
   task.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   task.ToolTip="写清楚要 DSH 做什么；可以直接说文件、目标、验收标准。";
   fullAccess=Ui.Check("完整访问：允许执行命令与写入工作区（谨慎）",pet.State.DshFullAccess,delegate(bool value) {pet.State.DshFullAccess=value;pet.Save();});
   fullAccess.Margin=new Thickness(0,0,0,14);
   start=Ui.Button("让 DSH 去做",StartTask,true);
   cancel=Ui.Button("取消当前任务",delegate {dsh.Cancel();},false);
   test=Ui.Button("测试连接",delegate {connection.Text="正在检查 DSH 桥接…";dsh.TestConnection();},false);
   open=Ui.Button("打开 DSH",OpenDsh,false);
   resultLabel=Ui.Text("当前任务",12,"#78616A");
   currentTitle=Ui.Text("还没有任务。",15,"#4F3B43");
   currentMeta=Ui.Text("",11,"#795F69");
   result=Ui.Input("",170);result.IsReadOnly=true;result.AcceptsReturn=true;result.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;result.VerticalContentAlignment=VerticalAlignment.Top;result.FontSize=12.5;
   result.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   historyList=new StackPanel();
   var head=Ui.Stack(
    Ui.Text("DSH 工作台",18,"#4F3B43"),
    Ui.Text("把实际动手的任务交给 DSH agent：它会创建真实 DSH 会话、使用工具读写工作区，完成后把结论带回桌宠。写操作需要你确认。",12,"#78616A"),
    status,connection,
    Ui.Row(test,open),
    Ui.Text("任务内容",12,"#78616A"),
    task,
    Ui.Row(start,cancel),
    fullAccess,
    Ui.Text("没有勾选「完整访问」时使用工作区可写权限；某些命令/工具可能需要完整访问。DSH 收到的是任务描述，不会收到 API Key。",11,"#795F69"));
   var current=Ui.Stack(resultLabel,currentTitle,currentMeta,result);
   var recent=Ui.Stack(Ui.Text("最近任务",18,"#4F3B43"),historyList);
   var root=new StackPanel();
   root.Children.Add(Ui.Card(head));
   root.Children.Add(Ui.Card(current));
   root.Children.Add(Ui.Card(recent));
   return root;
  }
  void StartTask() {
   string text=(task.Text??"").Trim();
   if(text.Length==0) {connection.Text="先写一点任务内容，再交给 DSH。";return;}
   if(!pet.State.DshEnabled) {connection.Text="DSH 委托在设置里关闭了，先启用它。";return;}
   string permission=pet.State.DshFullAccess?"danger-full-access":"workspace-write";
   string title="曳尔沐 · "+DshApi.Clip(text.Replace("\r"," ").Replace("\n"," "),28);
   if(dsh.Start(text,permission,title)) {
    connection.Text="已提交给 DSH，正在等它接线…";
    task.Clear();
   } else {
    connection.Text="没能提交："+(dsh.Current==null?"DSH 没有运行":dsh.Current.Error);
   }
  }
  void OpenDsh() {
   string error;var handshake=DshApi.ReadHandshake(out error);
   string url=handshake!=null&&!string.IsNullOrEmpty(handshake.webUrl)?handshake.webUrl:"http://127.0.0.1:3080";
   try {Process.Start(url);} catch(Exception e) {connection.Text="打开 DSH 失败："+e.Message;}
  }
  void OnConnection(bool ok,string text) {connection.Text=text;}
  void OnFinished(DshJob job) {
   if(job==null)return;
   if(job.Status=="done") connection.Text="● 任务完成 · "+job.Seconds.ToString("0.0")+"s · 工具 "+job.Tools+" 次";
   else if(job.Status=="cancelled") connection.Text="○ 任务已取消";
   else connection.Text="○ 任务没有完成："+(string.IsNullOrEmpty(job.Error)?"未知错误":job.Error);
  }
  void Refresh() {
   if(status==null)return;
   status.Text=dsh.StatusLine;
   start.IsEnabled=!dsh.Busy;
   cancel.IsEnabled=dsh.Busy;
   if(fullAccess!=null)fullAccess.IsChecked=pet.State.DshFullAccess;
   DshJob job=dsh.Current;
   if(job==null) {
    currentTitle.Text="还没有任务。";
    currentMeta.Text="写一件事，交给 DSH 工作台。";
    result.Text="";
   } else {
    currentTitle.Text=string.IsNullOrEmpty(job.Title)?DshApi.Clip(job.Task,48):job.Title;
    string stateText=StateText(job.Status);
    string meta="状态："+stateText+" · "+job.PermissionLabel;
    if(job.SessionId.Length>0)meta+=" · 会话 "+DshApi.Clip(job.SessionId,8)+"…";
    if(job.Tools>0)meta+=" · 工具 "+job.Tools+" 次";
    if(job.Seconds>0)meta+=" · "+job.Seconds.ToString("0.0")+"s";
    currentMeta.Text=meta;
    string body=job.Text;
    if(string.IsNullOrEmpty(body))body=job.Progress;
    if(string.IsNullOrEmpty(body))body=job.Status=="running"?"DSH 正在做，进度会显示在这里…":(job.Status=="queued"||job.Status=="starting"?"已排队，等待 DSH 接线…":"");
    if(!string.IsNullOrEmpty(job.Error))body=(body.Length>0?body+"\n\n":"")+"错误："+job.Error;
    if(!string.IsNullOrEmpty(job.Note))body=(body.Length>0?body+"\n\n":"")+job.Note;
    result.Text=body;
   }
   historyList.Children.Clear();
   DshJob[] history=dsh.History;
   if(history.Length==0) historyList.Children.Add(Ui.Text("还没有完成的任务。",11,"#795F69"));
   else {
    for(int i=history.Length-1;i>=0&&historyList.Children.Count<8;i--) {
     DshJob item=history[i];
     string line=StateIcon(item.Status)+"  "+DshApi.Clip(string.IsNullOrEmpty(item.Title)?item.Task:item.Title,42);
     var row=Ui.Text(line,11,"#78616A");
     row.Margin=new Thickness(0,0,0,5);
     row.ToolTip=item.SessionId.Length>0?"DSH 会话 "+item.SessionId:"";
     historyList.Children.Add(row);
    }
   }
  }
  static string StateText(string status) {
   if(status=="starting")return "准备中";
   if(status=="queued")return "排队中";
   if(status=="running")return "正在做";
   if(status=="cancelling")return "正在取消";
   if(status=="done")return "完成";
   if(status=="cancelled")return "已取消";
   if(status=="error")return "失败";
   return status??"";
  }
  static string StateIcon(string status) {
   if(status=="done")return "●";
   if(status=="error")return "×";
   if(status=="cancelled")return "○";
   if(status=="running"||status=="queued"||status=="starting")return "◌";
   return "·";
  }
  public void Dispose() {
   dsh.Changed-=Refresh;
   dsh.ConnectionChecked-=OnConnection;
   dsh.Finished-=OnFinished;
  }
 }
}
