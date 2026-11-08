using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ElymsPet {
 [DataContract] public sealed class PetState {
  [DataMember] public double Scale = 1;
  [DataMember] public double X = -99999;
  [DataMember] public double Y = -99999;
  [DataMember] public bool Topmost = true;
  [DataMember] public bool Wander = false;
  [DataMember] public bool Muted = true;
  [DataMember] public bool Sleeping = false;
  [DataMember] public bool RemindWater = false;
  [DataMember] public bool FullscreenQuiet = true;
  [DataMember] public bool WindowComments = false;
  [DataMember] public int Friendship = 0;
  [DataMember] public int FocusSessions = 0;
  [DataMember] public double Energy = 90;
  [DataMember] public string Note = "";
  [DataMember] public string FocusKind = "专注";
  [DataMember] public DateTime FocusEnd = new DateTime(0, DateTimeKind.Utc);
  [DataMember] public double PausedSeconds = 0;
  [DataMember] public int FocusMinutes = 25;
  [DataMember] public string ApiKey = "";
  [DataMember] public string ApiBase = "https://api.deepseek.com";
  [DataMember] public string ApiModel = "deepseek-flash";
  [DataMember] public bool AiChat = true;
  [DataMember] public bool AiThinking = false;
  [DataMember] public int Schema = 2;
  public void Normalize() {
   Scale = double.IsNaN(Scale) || double.IsInfinity(Scale) ? 1 : Math.Max(.65, Math.Min(1.6, Scale));
   Energy = double.IsNaN(Energy) ? 90 : Math.Max(0, Math.Min(100, Energy));
   if (double.IsNaN(X) || double.IsInfinity(X)) X = -99999;
   if (double.IsNaN(Y) || double.IsInfinity(Y)) Y = -99999;
   Note = Note ?? ""; FocusKind = FocusKind ?? "专注";
   FocusMinutes = Math.Max(1, Math.Min(180, FocusMinutes));
   PausedSeconds = Math.Max(0, Math.Min(10800, PausedSeconds));
   ApiKey = (ApiKey ?? "").Trim();
   if (ApiKey.Length > 200) ApiKey = ApiKey.Substring(0, 200);
   if (string.IsNullOrWhiteSpace(ApiBase)) ApiBase = "https://api.deepseek.com";
   ApiBase = ApiBase.Trim();
   if (ApiBase.Length > 300) ApiBase = ApiBase.Substring(0, 300);
   if (string.IsNullOrWhiteSpace(ApiModel)) ApiModel = "deepseek-flash";
   ApiModel = ApiModel.Trim();
   if (ApiModel.Length > 80) ApiModel = ApiModel.Substring(0, 80);
   if (Schema < 2) { Schema = 2; AiChat = true; }
  }
  public double Remaining(DateTime now) { return FocusEnd == DateTime.MinValue ? PausedSeconds : Math.Max(0,(FocusEnd-now).TotalSeconds); }
  public void StartFocus(int minutes, string kind, DateTime now) { FocusMinutes = Math.Max(1, Math.Min(180, minutes)); FocusKind = kind; PausedSeconds = 0; FocusEnd = now.AddMinutes(FocusMinutes); }
  public void TogglePause(DateTime now) {
   if (FocusEnd != DateTime.MinValue) { PausedSeconds = Remaining(now); FocusEnd = new DateTime(0, DateTimeKind.Utc); }
   else if (PausedSeconds > 0) { FocusEnd = now.AddSeconds(PausedSeconds); PausedSeconds = 0; }
  }
  public void CancelFocus() { FocusEnd=new DateTime(0, DateTimeKind.Utc); PausedSeconds=0; }
  public bool CompleteIfDue(DateTime now) { if(FocusEnd == DateTime.MinValue || now < FocusEnd) return false; if(FocusKind == "专注") FocusSessions++; CancelFocus(); return true; }
 }
 public static class Storage {
  public static string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElymsDesktopPet");
  public static string FilePath { get { return Path.Combine(Folder,"settings.json"); } }
  public static string LastError = "";
  public static PetState Load() {
   foreach(string path in new[]{FilePath,FilePath+".bak"}) {
    if(!File.Exists(path)) continue;
    try { using(var s=File.OpenRead(path)) { var v=(PetState)new DataContractJsonSerializer(typeof(PetState)).ReadObject(s); if(v!=null) { v.Normalize(); return v; } } }
    catch(Exception e) { LastError=e.Message; }
   }
   return new PetState();
  }
  public static bool Save(PetState state) {
   try {
    Directory.CreateDirectory(Folder); state.Normalize(); string temp=FilePath+".tmp";
    using(var s=File.Create(temp)) new DataContractJsonSerializer(typeof(PetState)).WriteObject(s,state);
    if(File.Exists(FilePath)) File.Replace(temp,FilePath,FilePath+".bak"); else File.Move(temp,FilePath);
    LastError=""; return true;
   } catch(Exception e) { LastError=e.Message; return false; }
  }
  public static void ForgetBackups() {
   string[] paths=new string[]{FilePath+".bak",FilePath+".tmp"};
   for(int i=0;i<paths.Length;i++) {
    try {if(File.Exists(paths[i]))File.Delete(paths[i]);} catch {}
   }
  }
 }
 public static class Dialogue {
  public static readonly string[] Idle = {
   "信号满格。今天的连接也很稳定呀。", "路过你的桌面，顺便留一个微笑。", "有趣的事，可以慢慢讲给我听。", "你的进度已经缓存，休息一下也没关系。",
   "如果灵感掉线了，我们就先喝一口水。", "小小的窗口，也装得下一次好好相遇。", "我正在这里，接收今天的好消息。", "不用想好开场白，直接说就可以啦。",
   "屏幕外的风景，也值得偶尔看一眼。", "有时候，安静陪着也是一种回应。", "把今天的一个小愿望，留在便签里吧。", "忙你的，我会在这里慢慢等。" };
  public static readonly string[] Touch = { "收到啦，一份来自你的友好信号。", "嗯？你是在确认我有没有掉线吗？", "这一下，我记到今天的好心情里了。", "连接成功，又多一点熟悉了。", "摸摸头也是一种通讯方式呀。", "在哦，回应速度是不是很快？" };
  public static readonly string[] Food = { "数据小点心，咔嚓！补给收到。", "谢谢招待，这份心意比点心还甜。", "缓存补充完成！一起继续今天吧。" };
  public static string Reply(string input) {
   string t=(input??"").Trim().ToLowerInvariant();
   if(t.Contains("晚安")||t.Contains("睡")) return "那就把今天暂时存档吧。晚安，明天再连接。";
   if(t.Contains("累")||t.Contains("烦")||t.Contains("难过")) return "收到你的心情了。先喘口气，好吗？不急着把一切处理完。";
   if(t=="y"||t.Contains("y是谁")||t.Contains("谁是y")||t.Contains("小y")||t.Contains("身份")) return "我是曳尔沐。借用过的形象，和一个人自己的经历，是两件事哦。";
   if(t.Contains("exe")) return "六队的工作还要配合进行呢。至于他的往事，那属于他和他记得的人。";
   if(t.Contains("灵可")) return "小灵可对有趣的东西总是很敏锐呢，下次也一起聊聊游戏吧。";
   if(t.Contains("工作")||t.Contains("学习")||t.Contains("专注")) return "我们先完成一小段吧。打开专注计时，我陪你守着这段时间。";
   if(t.Contains("喜欢")||t.Contains("谢谢")) return "这份心意接收到了。我也很高兴和你待在一起。";
   if(t.Contains("早")||t.Contains("你好")||t.Contains("在吗")) return "在这里哦。今天想从哪件小事开始？";
   if(t.Contains("生日")) return "我的资料上记着九月三日。能被朋友记住，也是很开心的事呀。";
   return "留言收到。我把它当作今天的一次小小联络。也可以写进便签，留给未来的自己。";
  }
 }
}
