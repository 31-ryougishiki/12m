using System;
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
   if(Array.IndexOf(args,"--self-test")>=0) return SelfTest.Run();
   int setKey=Array.IndexOf(args,"--set-key");
   if(setKey>=0) {
    if(setKey+1>=args.Length)return 2;
    string key=DeepSeek.CleanKey(args[setKey+1]);
    if(!DeepSeek.ValidKey(key))return 2;
    var state=Storage.Load();state.ApiKey=key;state.Normalize();
    bool saved=Storage.Save(state);if(saved)Storage.ForgetBackups();
    return saved?0:1;
   }
   if(Array.IndexOf(args,"--clear-key")>=0) {
    var state=Storage.Load();state.ApiKey="";
    bool saved=Storage.Save(state);if(saved)Storage.ForgetBackups();
    return saved?0:1;
   }
   if(Array.IndexOf(args,"--api-test")>=0) return ApiCheck(false,"");
   int chatArg=Array.IndexOf(args,"--chat");
   if(chatArg>=0) return ApiCheck(true,chatArg+1<args.Length?args[chatArg+1]:"你好");
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
  static int ApiCheck(bool chat,string prompt) {
   var state=Storage.Load();
   string origin;string key=DeepSeek.ResolveKey(state,out origin);
   ChatTurn[] turns=chat?new ChatSession().Build(state,prompt):DeepSeek.ConnectivityTurns();
   bool thinking=chat&&state.AiThinking;
   ChatOutcome outcome=DeepSeek.Complete(key,state.ApiBase,state.ApiModel,thinking,turns,60);
   Directory.CreateDirectory("tests");
   string path=Path.Combine("tests",chat?"chat-test.txt":"api-test.txt");
   var text=new StringBuilder();
   text.Append(outcome.Ok?"PASS":"FAIL").Append("\r\n");
   text.Append("model=").Append(state.ApiModel).Append("\r\n");
   text.Append("base=").Append(DeepSeek.NormalizeBaseUrl(state.ApiBase)).Append("\r\n");
   text.Append("key=").Append(DeepSeek.MaskKey(key)).Append(" (来源：").Append(origin).Append(")\r\n");
   text.Append("seconds=").Append(outcome.Seconds.ToString("0.00")).Append("\r\n");
   text.Append("status=").Append(outcome.Status).Append("\r\n");
   text.Append("tokens=").Append(outcome.PromptTokens).Append("+").Append(outcome.CompletionTokens).Append("\r\n");
   if(chat)text.Append("prompt=").Append(prompt).Append("\r\n");
   text.Append("reply=").Append(outcome.Text).Append("\r\n");
   text.Append("error=").Append(outcome.Error).Append("\r\n");
   text.Append("detail=").Append(outcome.Detail).Append("\r\n");
   File.WriteAllText(path,text.ToString(),Encoding.UTF8);
   return outcome.Ok?0:1;
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
    s.Note="曳尔沐 ≠ Y\n第二行：你好，朋友。";s.Scale=1.3;s.Friendship=7;s.AiChat=false;s.AiThinking=true;s.ApiKey="sk-test-state-123456789";s.ApiBase="https://api.deepseek.com";bool saved=Storage.Save(s);Assert(saved,"save: "+Storage.LastError);
    var loaded=Storage.Load();Assert(loaded.Note==s.Note&&loaded.Friendship==7&&loaded.Scale==1.3&&loaded.AiChat==false&&loaded.AiThinking&&loaded.ApiKey=="sk-test-state-123456789"&&loaded.ApiBase=="https://api.deepseek.com","unicode state roundtrip");
    s.Friendship=8;Storage.Save(s);File.WriteAllText(Storage.FilePath,"broken json");Assert(Storage.Load().Friendship==7,"corrupt settings recover backup");
    s.Scale=double.NaN;s.Energy=200;s.X=double.PositiveInfinity;s.Normalize();Assert(s.Scale==1&&s.Energy==100&&s.X==-99999,"sanitize invalid state");
    Assert(Dialogue.Reply("Y是谁").Contains("两件事"),"identity distinction");
    var sprite=new Sprite();Assert(sprite.Frames.Length==9&&sprite.Frames[0].PixelWidth>100,"embedded nine sprites");
    Assert(DeepSeek.NormalizeBaseUrl("api.deepseek.com/")=="https://api.deepseek.com"&&DeepSeek.Endpoint("https://api.deepseek.com/")=="https://api.deepseek.com/chat/completions","normalize endpoint");
    Assert(DeepSeek.ValidBaseUrl("https://api.deepseek.com")&&!DeepSeek.ValidBaseUrl("ftp://api.deepseek.com"),"validate base url");
    Assert(DeepSeek.MaskKey("sk-1234567890abcdef").EndsWith("cdef")&&!DeepSeek.MaskKey("sk-1234567890abcdef").Contains("567890"),"mask key");
    string persona=DeepSeek.BuildSystemPrompt(s,now);Assert(persona.Contains("不是小 Y 长大的样子")&&persona.Contains("不能真的操作电脑")&&!persona.Contains("sk-"),"persona boundary");
    string sample="{\"id\":\"x\",\"object\":\"chat.completion\",\"model\":\"deepseek-flash\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"接通啦～\",\"reasoning_content\":\"不发送\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":6,\"total_tokens\":18}}";
    ChatOutcome good=DeepSeek.ParseResponse(sample,0.5);Assert(good.Ok&&good.Text=="接通啦～"&&good.CompletionTokens==6,"parse chat response");
    ChatOutcome apiError=DeepSeek.ParseResponse("{\"error\":{\"message\":\"Authentication Fails\",\"type\":\"authentication_error\",\"code\":\"invalid_request_error\"}}",0.2);Assert(!apiError.Ok&&apiError.Detail.Contains("Authentication"),"parse api error");
    Assert(!DeepSeek.ParseResponse("{}",0.1).Ok,"empty response guarded");
    ChatSession session=new ChatSession();for(int i=0;i<10;i++)session.Remember("问题"+i,"回答"+i);Assert(session.Count==16,"chat history capped");session.Clear();Assert(session.Count==0,"chat history clear");
    var keyState=new PetState();keyState.ApiKey="sk-state-key-123456789";string origin;Assert(DeepSeek.ResolveKey(keyState,out origin)=="sk-state-key-123456789"&&origin=="设置文件","state key priority");
    string oldEnv=Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY",null);
    File.WriteAllText(Path.Combine(temp,"api-key.txt"),"DEEPSEEK_API_KEY=sk-file-key-123456789\r\n");
    Assert(DeepSeek.ResolveKey(new PetState(),out origin)=="sk-file-key-123456789"&&origin.Contains("api-key.txt"),"key file fallback");
    File.Delete(Path.Combine(temp,"api-key.txt"));if(oldEnv!=null)Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY",oldEnv);
    using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{\"Scale\":1}"))) {var legacy=(PetState)new DataContractJsonSerializer(typeof(PetState)).ReadObject(stream);legacy.Normalize();Assert(legacy.Schema==2&&legacy.AiChat&&legacy.ApiBase=="https://api.deepseek.com"&&legacy.ApiModel=="deepseek-flash","legacy settings get AI defaults");}
    Directory.CreateDirectory("tests");File.WriteAllText("tests/results.txt","PASS: "+checks+" checks\r\nTimer start/pause/resume/completion/cancel, break counts, limits, Unicode persistence, backup recovery, state validation, identity boundary, embedded sprites, DeepSeek JSON/error/key handling, history cap, legacy AI defaults.\r\n");return 0;
   }catch(Exception e){Directory.CreateDirectory("tests");File.WriteAllText("tests/results.txt","FAIL: "+e);return 1;}
   finally {if(Directory.Exists(temp))Directory.Delete(temp,true);}
  }
 }
}
