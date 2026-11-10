using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ElymsPet {
 static class Program {
  static Mutex mutex;
  static EventWaitHandle recall;
  [STAThread] static int Main(string[] args) {
   if(Array.IndexOf(args,"--self-test")>=0) return DiagnosticCommands.RunOfflineTests();
   int setKey=Array.IndexOf(args,"--set-key");
   if(setKey>=0) {
    if(setKey+1>=args.Length)return 2;
    string key=ApiKey.Clean(args[setKey+1]);
    if(!ApiKey.Valid(key))return 2;
    var state=Storage.Load();state.ApiKey=key;state.Normalize();
    bool saved=Storage.Save(state);if(saved)Storage.ForgetBackups();
    return saved?0:1;
   }
   if(Array.IndexOf(args,"--clear-key")>=0) {
    var state=Storage.Load();state.ApiKey="";
    bool saved=Storage.Save(state);if(saved)Storage.ForgetBackups();
    return saved?0:1;
   }
   if(Array.IndexOf(args,"--api-test")>=0) return DiagnosticCommands.ApiCheck(false,"");
   int chatArg=Array.IndexOf(args,"--chat");
   if(chatArg>=0) return DiagnosticCommands.ApiCheck(true,chatArg+1<args.Length?args[chatArg+1]:"你好");
   int webArg=Array.IndexOf(args,"--web-test");
   if(webArg>=0) return DiagnosticCommands.WebCheck(false,webArg+1<args.Length?args[webArg+1]:"曳尔沐");
   int fetchArg=Array.IndexOf(args,"--web-fetch");
   if(fetchArg>=0) return DiagnosticCommands.WebCheck(true,fetchArg+1<args.Length?args[fetchArg+1]:"https://api-docs.deepseek.com/");
   int webChatArg=Array.IndexOf(args,"--web-chat");
   if(webChatArg>=0) return DiagnosticCommands.WebChatCheck(webChatArg+1<args.Length?args[webChatArg+1]:"DeepSeek 最新模型是什么");
   int biliArg=Array.IndexOf(args,"--bili-test");
   if(biliArg>=0) return DiagnosticCommands.BiliCheck(biliArg+1<args.Length?args[biliArg+1]:"独立游戏 开发");
   int biliChatArg=Array.IndexOf(args,"--bili-chat");
   if(biliChatArg>=0) return DiagnosticCommands.BiliChatCheck(biliChatArg+1<args.Length?args[biliChatArg+1]:"");
   int biliDelArg=Array.IndexOf(args,"--bili-delete");
   if(biliDelArg>=0) return DiagnosticCommands.BiliDeleteCheck(biliDelArg+1<args.Length?args[biliDelArg+1]:"",biliDelArg+2<args.Length?args[biliDelArg+2]:"");
   if(Array.IndexOf(args,"--dsh-test")>=0) return DiagnosticCommands.DshCheck();
   if(Array.IndexOf(args,"--ui-smoke")>=0) return DiagnosticCommands.UiSmoke();
   if(Array.IndexOf(args,"--persona")>=0) return DiagnosticCommands.PersonaDump();
   if(Array.IndexOf(args,"--memory-bench")>=0) return DiagnosticCommands.MemoryBench();
   int archiveArg=Array.IndexOf(args,"--archive");
   if(archiveArg>=0) return DiagnosticCommands.ArchiveCheck(archiveArg+1<args.Length?args[archiveArg+1]:"");
   if(Array.IndexOf(args,"--archive-init")>=0) return DiagnosticCommands.ArchiveInit();
   bool preview=Array.IndexOf(args,"--render")>=0;
   System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
   bool first;
   mutex=new Mutex(true,"Local\\ElymsDesktopPet.v1",out first);
   if(!first&&!preview) {try{EventWaitHandle.OpenExisting("Local\\ElymsDesktopPet.Recall").Set();}catch{}return 0;}
   var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
   MenuTheme.Install(app);
   app.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
    string detail=e.Exception==null?"未知异常":e.Exception.ToString();
    string message=e.Exception==null?"未知异常":e.Exception.Message;
    try {
     string key=ApiKey.Resolve(Storage.Load()).Key;
     if(!string.IsNullOrEmpty(key)) {detail=detail.Replace(key,ApiKey.Mask(key));message=message.Replace(key,ApiKey.Mask(key));}
    } catch {}
    try {Directory.CreateDirectory(Storage.Folder);File.AppendAllText(Path.Combine(Storage.Folder,"error.log"),DateTime.Now+" "+detail+Environment.NewLine);}catch{}
    MessageBox.Show("曳尔沐遇到了一个问题，详情已写入本地 error.log。\n"+message,"曳尔沐桌宠");
    e.Handled=true;app.Shutdown(1);
   };
   var pet=new PetWindow(preview?new PetState():Storage.Load(),preview);app.MainWindow=pet;
   if(!preview) {
    recall=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ElymsDesktopPet.Recall");
    var thread=new Thread(delegate(){while(true){recall.WaitOne();if(!app.Dispatcher.HasShutdownStarted)app.Dispatcher.BeginInvoke(new Action(pet.Recover));}});thread.IsBackground=true;thread.Start();
   }
   pet.Show();
   if(preview) {
    string folder=Path.GetFullPath("preview");Directory.CreateDirectory(folder);
    pet.Art.Frame=0;pet.Say("今天，也保持联络。",600);pet.UpdateLayout();DiagnosticCommands.Render(pet,Path.Combine(folder,"pet.png"));
    pet.Conversation.SeedPreview("今天这一章终于写完了。","辛苦啦。要不要先把这一页存下来，给眼睛放个小假？");
    var panel=new ControlWindow(pet);panel.Show();panel.Refresh();panel.UpdateLayout();DiagnosticCommands.Render(panel,Path.Combine(folder,"control-panel.png"));
    for(int i=1;i<panel.Tabs.Items.Count;i++){panel.Tabs.SelectedIndex=i;panel.UpdateLayout();DiagnosticCommands.Render(panel,Path.Combine(folder,"tab-"+i+".png"));}
    panel.Width=760;panel.Height=560;panel.Tabs.SelectedIndex=0;panel.UpdateLayout();DiagnosticCommands.Render(panel,Path.Combine(folder,"control-panel-compact.png"));
    panel.SelectTab(PanelTab.Settings);panel.UpdateLayout();DiagnosticCommands.Render(panel,Path.Combine(folder,"settings-compact.png"));
    var bar=new ChatBarWindow(pet);bar.ShowBar();bar.UpdateLayout();DiagnosticCommands.Render(bar,Path.Combine(folder,"chat-bar-render.png"));bar.ShowArchive();bar.UpdateLayout();DiagnosticCommands.Render(bar,Path.Combine(folder,"chat-bar-archive-render.png"));bar.Close();
    var menu=pet.BuildMenu();menu.PlacementTarget=pet;menu.IsOpen=true;menu.UpdateLayout();DiagnosticCommands.Render(menu,Path.Combine(folder,"pet-menu.png"));
    var behavior=(System.Windows.Controls.MenuItem)menu.Items[6];behavior.IsSubmenuOpen=true;behavior.UpdateLayout();
    var submenu=(System.Windows.Controls.Primitives.Popup)behavior.Template.FindName("PART_Popup",behavior);
    var submenuRoot=(FrameworkElement)submenu.Child;submenuRoot.UpdateLayout();DiagnosticCommands.Render(submenuRoot,Path.Combine(folder,"pet-menu-settings.png"));
    behavior.IsSubmenuOpen=false;menu.IsOpen=false;
    var sample=new System.Windows.Controls.TextBox{Text="这是一条用于预览的消息。"};
    var editHost=new Window{Content=sample,Width=300,Height=100,ShowInTaskbar=false};editHost.Show();editHost.UpdateLayout();sample.Focus();sample.SelectAll();
    var copyMenu=ChatView.MessageMenu(sample,sample.Text);copyMenu.PlacementTarget=pet;copyMenu.IsOpen=true;copyMenu.UpdateLayout();DiagnosticCommands.Render(copyMenu,Path.Combine(folder,"message-menu.png"));copyMenu.IsOpen=false;
    var editMenu=MenuTheme.EditMenu(sample);editMenu.PlacementTarget=pet;editMenu.IsOpen=true;System.Windows.Input.CommandManager.InvalidateRequerySuggested();app.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(delegate {}));editMenu.UpdateLayout();DiagnosticCommands.Render(editMenu,Path.Combine(folder,"edit-menu.png"));editMenu.IsOpen=false;editHost.Close();
    var prompt=new PromptWindow("删除这个存档及其中的画像记录？\n这个操作不能撤销。","删除存档",MessageBoxButton.YesNo,MessageBoxImage.Warning);prompt.Show();prompt.UpdateLayout();DiagnosticCommands.Render(prompt,Path.Combine(folder,"confirmation.png"));prompt.Close();
    panel.Close();pet.Close();return 0;
   }
   if(Array.IndexOf(args,"--panel")>=0)pet.OpenPanel();
   if(Array.IndexOf(args,"--chatbar")>=0)pet.OpenChatBar();
   if(Array.IndexOf(args,"--sleep")>=0&&!pet.State.Sleeping)pet.SleepToggle();
   app.Run();return 0;
  }
 }
}
