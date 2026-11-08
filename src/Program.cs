using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ElymsPet {
 static class Program {
  static Mutex mutex;
  static EventWaitHandle recall;
  [STAThread] static int Main(string[] args) {
   if(Array.IndexOf(args,"--self-test")>=0) return SelfTest.Run();
   bool preview=Array.IndexOf(args,"--render")>=0;
   System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
   bool first;
   mutex=new Mutex(true,"Local\\ElymsDesktopPet.v1",out first);
   if(!first&&!preview) {try{EventWaitHandle.OpenExisting("Local\\ElymsDesktopPet.Recall").Set();}catch{}return 0;}
   var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};
   app.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
    try {Directory.CreateDirectory(Storage.Folder);File.AppendAllText(Path.Combine(Storage.Folder,"error.log"),DateTime.Now+" "+e.Exception+Environment.NewLine);}catch{}
    MessageBox.Show("曳尔沐遇到了一个问题，详情已写入本地 error.log。\n"+e.Exception.Message,"曳尔沐桌宠");
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
    pet.Art.Frame=0;pet.Say("今天，也保持联络。",600);pet.UpdateLayout();Render(pet,Path.Combine(folder,"pet.png"));
    var panel=new ControlWindow(pet);panel.Show();panel.Refresh();panel.UpdateLayout();Render(panel,Path.Combine(folder,"control-panel.png"));
    for(int i=1;i<panel.Tabs.Items.Count;i++){panel.Tabs.SelectedIndex=i;panel.UpdateLayout();Render(panel,Path.Combine(folder,"tab-"+i+".png"));}
    panel.Close();pet.Close();return 0;
   }
   if(Array.IndexOf(args,"--panel")>=0)pet.OpenPanel();
   if(Array.IndexOf(args,"--sleep")>=0&&!pet.State.Sleeping)pet.SleepToggle();
   app.Run();return 0;
  }
  static void Render(FrameworkElement element,string path) {
   var window=element as Window;if(window!=null&&window.Content is FrameworkElement)element=(FrameworkElement)window.Content;
   var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var s=File.Create(path))encoder.Save(s);
  }
 }
 static class SelfTest {
  static int checks;
  static void Assert(bool value,string name) {if(!value)throw new Exception(name);checks++;}
  public static int Run() {
   string temp=Path.Combine(Path.GetTempPath(),"ElymsPetTest-"+Guid.NewGuid().ToString("N"));Storage.Folder=temp;
   try {
    DateTime now=new DateTime(2026,11,9,12,0,0,DateTimeKind.Utc);var s=new PetState();
    s.StartFocus(25,"专注",now);Assert(s.Remaining(now)==1500,"start duration");
    s.TogglePause(now.AddMinutes(10));Assert(s.FocusEnd==DateTime.MinValue&&s.PausedSeconds==900,"pause preserves remaining");
    s.TogglePause(now.AddMinutes(20));Assert(s.FocusEnd==now.AddMinutes(35),"resume deadline");
    Assert(!s.CompleteIfDue(now.AddMinutes(34)),"no early completion");Assert(s.CompleteIfDue(now.AddMinutes(35))&&s.FocusSessions==1,"one completion");Assert(!s.CompleteIfDue(now.AddMinutes(36))&&s.FocusSessions==1,"no duplicate completion");
    s.StartFocus(5,"休息",now);s.CompleteIfDue(now.AddMinutes(6));Assert(s.FocusSessions==1,"break not counted as focus");
    s.StartFocus(999,"专注",now);Assert(s.Remaining(now)==10800,"duration cap");s.CancelFocus();Assert(s.Remaining(now)==0,"cancel");
    s.Note="曳尔沐 ≠ Y\n第二行：你好，朋友。";s.Scale=1.3;s.Friendship=7;bool saved=Storage.Save(s);Assert(saved,"save: "+Storage.LastError);
    var loaded=Storage.Load();Assert(loaded.Note==s.Note&&loaded.Friendship==7&&loaded.Scale==1.3,"unicode state roundtrip");
    s.Friendship=8;Storage.Save(s);File.WriteAllText(Storage.FilePath,"broken json");Assert(Storage.Load().Friendship==7,"corrupt settings recover backup");
    s.Scale=double.NaN;s.Energy=200;s.X=double.PositiveInfinity;s.Normalize();Assert(s.Scale==1&&s.Energy==100&&s.X==-99999,"sanitize invalid state");
    Assert(Dialogue.Reply("Y是谁").Contains("两件事"),"identity distinction");
    var sprite=new Sprite();Assert(sprite.Frames.Length==9&&sprite.Frames[0].PixelWidth>100,"embedded nine sprites");
    Directory.CreateDirectory("tests");File.WriteAllText("tests/results.txt","PASS: "+checks+" checks\r\nTimer start/pause/resume/completion/cancel, break counts, limits, Unicode persistence, backup recovery, state validation, identity boundary, embedded sprites.\r\n");return 0;
   }catch(Exception e){Directory.CreateDirectory("tests");File.WriteAllText("tests/results.txt","FAIL: "+e);return 1;}
   finally {if(Directory.Exists(temp))Directory.Delete(temp,true);}
  }
 }
}
