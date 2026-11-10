using System;
using System.IO;
using System.Runtime.Serialization;

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
  [DataMember] public bool AiThinking = true;
  [DataMember] public double ChatBarX = -99999;
  [DataMember] public double ChatBarY = -99999;
  [DataMember] public bool ChatBarHistory = true;
  [DataMember] public bool ChatBarAutoHide = false;
  [DataMember] public bool DshEnabled = true;
  [DataMember] public bool DshAutoRun = false;
  [DataMember] public bool DshFullAccess = false;
  [DataMember] public bool WebEnabled = true;
  [DataMember] public bool WebNeedContent = true;
  [DataMember] public bool AutoLife = true;
  [DataMember] public int AutoLifeMinutes = 30;
  [DataMember] public bool AutoLifeDsh = false;
  [DataMember] public bool MemoryEnabled = true;
  [DataMember] public int Schema = 8;
  public PetState Snapshot() { return (PetState)MemberwiseClone(); }
  public void Normalize() {
   Scale = double.IsNaN(Scale) || double.IsInfinity(Scale) ? 1 : Math.Max(.65, Math.Min(1.6, Scale));
   Energy = double.IsNaN(Energy) ? 90 : Math.Max(0, Math.Min(100, Energy));
   if (double.IsNaN(X) || double.IsInfinity(X)) X = -99999;
   if (double.IsNaN(Y) || double.IsInfinity(Y)) Y = -99999;
   if (double.IsNaN(ChatBarX) || double.IsInfinity(ChatBarX)) ChatBarX = -99999;
   if (double.IsNaN(ChatBarY) || double.IsInfinity(ChatBarY)) ChatBarY = -99999;
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
   if (Schema < 3) { Schema = 3; ChatBarX = -99999; ChatBarY = -99999; ChatBarHistory = true; ChatBarAutoHide = false; }
   if (Schema < 4) { Schema = 4; DshEnabled = true; DshAutoRun = false; DshFullAccess = false; }
   if (Schema < 5) { Schema = 5; WebEnabled = true; WebNeedContent = true; }
   if (Schema < 6) { Schema = 6; AiThinking = true; }
   if (Schema < 7) { Schema = 7; AutoLife = true; AutoLifeMinutes = 30; AutoLifeDsh = false; }
   if (Schema < 8) { Schema = 8; MemoryEnabled = true; }
   AutoLifeMinutes = Math.Max(10, Math.Min(120, AutoLifeMinutes));
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
   PetState value=JsonFile.Load<PetState>(FilePath,out LastError);
   if(value==null) return new PetState();
   value.Normalize();
   return value;
  }
  public static bool Save(PetState state) {
   state.Normalize();
   return JsonFile.Save(FilePath,state,Folder,out LastError);
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
   "在吗在吗？不急着回，我只是路过来打个招呼。", "今天有什么小插曲？无聊的那种也算。", "有趣的事，可以慢慢讲给我听。", "忙完这一段，要不要给自己放个小假？",
   "我有点好奇，你会保留已经通关的游戏吗？", "要是给今天起个标题，你会选什么？", "有没有什么事，做之前嫌麻烦，做完却很开心？", "不用想好开场白，直接说就可以啦。",
   "屏幕外的风景，也值得偶尔看一眼。", "有时候，安静陪着也是一种回应。", "想起什么有趣的小事，可以写进便签，免得一会儿忘了。", "忙你的，有事再叫我。" };
  public static readonly string[] Touch = { "嗯？找我有事，还是随便打个招呼？", "接通啦，这次轮到你先说。", "好好，我知道你在这里啦。", "这么熟练，看来不是第一次找我了？", "诶，副队长的发型也要维护一下嘛。", "在哦，回应速度是不是很快？" };
  public static readonly string[] Food = { "数据小点心，咔嚓！补给收到。", "谢谢招待，这份心意比点心还甜。", "这下有精神啦！一起继续今天吧。" };
  public static string Reply(string input) {
   string t=(input??"").Trim().ToLowerInvariant();
   if(t.Contains("别说话")||t.Contains("别打扰")||t.Contains("我忙")) return "好，你忙你的。有事再叫我。";
   if(t.Contains("晚安")||t.Contains("睡")) return "好，今天就到这里。晚安，剩下的事明天再说。";
   if(t.Contains("累")||t.Contains("烦")||t.Contains("难过")) return "收到你的心情了。先喘口气，好吗？不急着把一切处理完。";
   if(MentionsY(t)||t.Contains("身份")) return "我是曳尔沐。借用过的形象，和一个人自己的经历，是两件事哦。";
   if(t.Contains("exe")) return "六队的工作还要配合进行呢。至于他的往事，那属于他和他记得的人。";
   if(t.Contains("灵可")) return "小灵可对有趣的东西总是很敏锐呢，下次也一起聊聊游戏吧。";
   if(t.Contains("工作")||t.Contains("学习")||t.Contains("专注")) return "我们先完成一小段吧。打开专注计时，我陪你守着这段时间。";
   if(t.Contains("喜欢")||t.Contains("谢谢")) return "这份心意接收到了。我也很高兴和你待在一起。";
   if(t.Contains("早")||t.Contains("你好")||t.Contains("在吗")) return "在这里哦。今天想从哪件小事开始？";
   if(t.Contains("生日")) return "九月三日，是我作为人入职管理局的纪念日哦。不过和朋友一起庆祝，哪天也不错吧？";
   return "留言收到。我把它当作今天的一次小小联络。也可以写进便签，留给未来的自己。";
  }
  /// <summary>单独出现的 Y（前后不是英文字母），避免把 you、type 这类单词当成她。</summary>
  static bool MentionsY(string text) {
   for(int i=0;i<text.Length;i++) {
    if(text[i]!='y') continue;
    bool left=i==0||!IsAsciiLetter(text[i-1]);
    bool right=i==text.Length-1||!IsAsciiLetter(text[i+1]);
    if(left&&right) return true;
   }
   return false;
  }
  static bool IsAsciiLetter(char c) {return (c>='a'&&c<='z')||(c>='A'&&c<='Z');}
 }
}
