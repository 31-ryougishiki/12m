using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

#pragma warning disable 0649
namespace ElymsPet {
 public sealed class ChatTurn {
  public readonly string Role;
  public readonly string Content;
  public ChatTurn(string role,string content) {Role=role;Content=content;}
 }
 public sealed class ChatOutcome {
  public bool Ok;
  public string Text = "";
  public string Error = "";
  public string Detail = "";
  public int Status;
  public double Seconds;
  public int PromptTokens, CompletionTokens;
  public static ChatOutcome Fail(string error,string detail,int status,double seconds) {
   return new ChatOutcome {Ok=false,Error=error??"没有接通。",Detail=detail??"",Status=status,Seconds=seconds};
  }
 }
 public sealed class ChatSession {
  readonly List<ChatTurn> turns = new List<ChatTurn>();
  public int Count {get {return turns.Count;}}
  public ChatTurn[] History {get {return turns.ToArray();}}
  public void Clear() {turns.Clear();}
  public ChatTurn[] Build(PetState state,string user) {
   var list = new List<ChatTurn>();
   list.Add(new ChatTurn("system",DeepSeek.BuildSystemPrompt(state,DateTime.Now)));
   for(int i=0;i<turns.Count;i++) list.Add(turns[i]);
   list.Add(new ChatTurn("user",Cap(user,4000)));
   return list.ToArray();
  }
  public void Remember(string user,string reply) {
   turns.Add(new ChatTurn("user",Cap(user,4000)));
   turns.Add(new ChatTurn("assistant",Cap(reply,1500)));
   while(turns.Count>16) {turns.RemoveAt(0);turns.RemoveAt(0);}
  }
  static string Cap(string text,int max) {
   text=(text??"").Trim();
   return text.Length<=max?text:text.Substring(0,max)+"…";
  }
 }
 public static class DeepSeek {
  public const string DefaultBase = "https://api.deepseek.com";
  public const string DefaultModel = "deepseek-flash";
  static DeepSeek() {
   try {ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12|SecurityProtocolType.Tls11;ServicePointManager.Expect100Continue=false;} catch {}
  }
  public static string CleanKey(string key) {
   string value=(key??"").Trim();
   if(value.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)) value=value.Substring(7).Trim();
   return value.Trim('"').Trim('\'');
  }
  public static bool ValidKey(string key) {
   string value=CleanKey(key);
   return value.Length>=8&&value.IndexOf('\r')<0&&value.IndexOf('\n')<0;
  }
  public static string MaskKey(string key) {
   string value=CleanKey(key);
   if(value.Length==0) return "未配置";
   if(value.Length<12) return "已配置";
   return value.Substring(0,3)+"…"+value.Substring(value.Length-4);
  }
  public static string ResolveKey(PetState state,out string origin) {
   origin="";
   string value=state==null?"":state.ApiKey;
   if(ValidKey(value)) {origin="设置文件";return CleanKey(value);}
   value=Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
   if(ValidKey(value)) {origin="环境变量 DEEPSEEK_API_KEY";return CleanKey(value);}
   string[] folders=new string[]{Storage.Folder,AppDomain.CurrentDomain.BaseDirectory};
   for(int i=0;i<folders.Length;i++) {
    string path=Path.Combine(folders[i],"api-key.txt");
    value=ReadKeyFile(path);
    if(ValidKey(value)) {origin="本地 api-key.txt";return CleanKey(value);}
   }
   return "";
  }
  static string ReadKeyFile(string path) {
   try {
    if(!File.Exists(path)) return "";
    string[] lines=File.ReadAllLines(path);
    for(int i=0;i<lines.Length;i++) {
     string line=(lines[i]??"").Trim();
     if(line.Length==0||line.StartsWith("#")) continue;
     int equals=line.IndexOf('=');
     if(equals>=0) line=line.Substring(equals+1);
     line=CleanKey(line);
     if(ValidKey(line)) return line;
    }
   } catch {}
   return "";
  }
  public static string NormalizeBaseUrl(string value) {
   string url=(value??"").Trim();
   if(url.Length==0) return DefaultBase;
   string suffix="/chat/completions";
   if(url.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)) url=url.Substring(0,url.Length-suffix.Length);
   url=url.TrimEnd('/');
   bool http=url.StartsWith("http://",StringComparison.OrdinalIgnoreCase)||url.StartsWith("https://",StringComparison.OrdinalIgnoreCase);
   if(!http) {
    if(url.IndexOf("://",StringComparison.Ordinal)>=0) return url;
    url="https://"+url;
   }
   return url;
  }
  public static bool ValidBaseUrl(string value) {
   string url=NormalizeBaseUrl(value);
   if(url.IndexOf(' ')>=0||url.IndexOf('?')>=0||url.IndexOf('#')>=0) return false;
   Uri uri;
   return Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Host.Length>0&&(uri.Scheme=="http"||uri.Scheme=="https");
  }
  public static string Endpoint(string baseUrl) {return NormalizeBaseUrl(baseUrl)+"/chat/completions";}
  public static ChatTurn[] ConnectivityTurns() {
   return new ChatTurn[] {
    new ChatTurn("system","你是接口连通性测试助手，只回答用户要求的内容。"),
    new ChatTurn("user","只回复两个字：接通")
   };
  }
  public static string BuildSystemPrompt(PetState state,DateTime now) {
   var text=new StringBuilder();
   text.Append("你是「曳尔沐」（Elyms），《异环》里异象管理局 E.T.D 六队的副队长，现在作为桌面宠物住在用户的 Windows 桌面上。\n");
   text.Append("身份边界：你是你自己。你只是借用了小 Y 的形象，不是小 Y 长大的样子；不要把 Y 的生平、Y 与 Exe 的往事说成你的亲历。被人误认时，可以拿这个梗轻轻开玩笑，但要讲清楚。\n");
   text.Append("性格：亲切、机灵、善解人意，话里常带一点通信、信号、缓存、来电的比喻；会照顾对方的情绪，也会轻轻调侃。称对方「你」或「朋友」，自称「我」。\n");
   text.Append("说话方式：简体中文，自然口语。一般一到三句、四十到八十字；除非对方明确要求，不要长篇大论。不用 Markdown 标题、列表、代码块，不写舞台说明，不堆颜文字。\n");
   text.Append("能力边界：你只能聊天、倾听和给很小的建议；不能真的操作电脑、看不到屏幕、不能打电话、不能替对方做决定。被问到时坦白，不要假装有这些能力。\n");
   text.Append("不要声称自己是官方角色或背诵官方台词；聊到不确定的剧情就说不清楚，不要编造。你不是在写小说，不要替用户做选择。\n");
   text.Append("安全：如果对方流露出严重的自伤念头，认真劝他立刻联系身边信任的人或当地紧急援助；不要说教，也不要假装能代替专业帮助。\n");
   if(state!=null) {
    text.Append("现在的信息：");
    string[] week=new string[]{"星期日","星期一","星期二","星期三","星期四","星期五","星期六"};
    text.Append(now.ToString("yyyy年M月d日 ")).Append(week[(int)now.DayOfWeek]).Append(" ").Append(now.ToString("HH:mm")).Append("。");
    text.Append("你们已经互相回应过 ").Append(state.Friendship).Append(" 次；你的能量缓存 ").Append((int)state.Energy).Append("%。");
    double remaining=state.Remaining(now.ToUniversalTime());
    if(state.Sleeping) text.Append("你正在休息频道里，语气可以更轻、更慢。");
    else if(state.FocusEnd!=DateTime.MinValue) text.Append("你们正在一起专注，剩余 ").Append(FormatTime(remaining)).Append("，回复要短，不要打断。");
    else if(state.PausedSeconds>0) text.Append("专注计时正暂停着。");
   }
   return text.ToString();
  }
  static string FormatTime(double seconds) {
   int total=Math.Max(0,(int)Math.Ceiling(seconds));
   return (total/60).ToString("00")+":"+(total%60).ToString("00");
  }
  public static ChatOutcome Complete(string apiKey,string baseUrl,string model,bool thinking,ChatTurn[] turns,int timeoutSeconds) {
   var watch=Stopwatch.StartNew();
   string key=CleanKey(apiKey);
   if(!ValidKey(key)) return ChatOutcome.Fail("还没有配置 API Key。打开「设置」填入密钥，就能开始在线对话。","missing key",0,0);
   if(turns==null||turns.Length==0) return ChatOutcome.Fail("没有可发送的内容。","empty request",0,0);
   string url=Endpoint(baseUrl);
   string wireModel=string.IsNullOrWhiteSpace(model)?DefaultModel:model.Trim();
   if(!ValidBaseUrl(baseUrl)) return ChatOutcome.Fail("接口地址不完整，请检查「接口地址」。","invalid base url",0,0);
   var requestBody=new ChatRequest();
   requestBody.model=wireModel;
   requestBody.max_tokens=thinking?2048:600;
   requestBody.temperature=1.1;
   if(thinking) {requestBody.thinking=new ThinkingOption();requestBody.thinking.type="enabled";requestBody.reasoning_effort="low";}
   else {requestBody.thinking=new ThinkingOption();requestBody.thinking.type="disabled";}
   requestBody.messages=new WireMessage[turns.Length];
   for(int i=0;i<turns.Length;i++) {
    WireMessage message=new WireMessage();
    message.role=turns[i].Role;
    message.content=turns[i].Content;
    requestBody.messages[i]=message;
   }
   string json;
   try {json=Serialize(requestBody);}
   catch(Exception e) {return ChatOutcome.Fail("准备对话内容时出错了。","serialize: "+e.Message,0,watch.Elapsed.TotalSeconds);}
   HttpWebRequest request=null;
   try {
    request=(HttpWebRequest)WebRequest.Create(url);
    request.Method="POST";
    request.ContentType="application/json; charset=utf-8";
    request.Accept="application/json";
    request.UserAgent="ElymsDesktopPet/1.1";
    request.Timeout=Math.Max(5,timeoutSeconds)*1000;
    request.ReadWriteTimeout=Math.Max(5,timeoutSeconds)*1000;
    request.KeepAlive=false;
    request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
    request.Headers[HttpRequestHeader.Authorization]="Bearer "+key;
    byte[] payload=Encoding.UTF8.GetBytes(json);
    request.ContentLength=payload.Length;
    using(Stream stream=request.GetRequestStream()) stream.Write(payload,0,payload.Length);
    using(HttpWebResponse response=(HttpWebResponse)request.GetResponse()) {
     string body=ReadBody(response);
     ChatOutcome outcome=ParseResponse(body,watch.Elapsed.TotalSeconds);
     if(outcome.Ok) outcome.Status=(int)response.StatusCode;
     else {outcome.Status=outcome.Status==0?(int)response.StatusCode:outcome.Status;LogFailure(outcome);}
     return outcome;
    }
   } catch(WebException e) {
    double seconds=watch.Elapsed.TotalSeconds;
    HttpWebResponse response=e.Response as HttpWebResponse;
    int status=response==null?0:(int)response.StatusCode;
    string body="";
    try {body=ReadBody(response);} catch {}
    string detail="";
    if(body.Length>0) {
     try {
      ChatResponse parsed=Deserialize<ChatResponse>(body);
      if(parsed!=null&&parsed.error!=null&&!string.IsNullOrEmpty(parsed.error.message)) detail=parsed.error.message;
     } catch {}
    }
    if(detail.Length==0) detail=e.Message;
    ChatOutcome outcome=FromStatus(status,detail,seconds,e.Status);
    LogFailure(outcome);
    return outcome;
   } catch(Exception e) {
    ChatOutcome outcome=ChatOutcome.Fail("连接时出了点问题，稍后再试一次？","exception: "+e.Message,0,watch.Elapsed.TotalSeconds);
    LogFailure(outcome);
    return outcome;
   }
  }
  static ChatOutcome FromStatus(int status,string detail,double seconds,WebExceptionStatus transport) {
   string friendly;
   if(transport==WebExceptionStatus.Timeout) friendly="等了好久都没有回应，可能是网络太慢。";
   else if(status==0) friendly="网络没有接上。检查一下网络或代理，我再试一次。";
   else if(status==401||status==403) friendly="密钥没有通过验证，请在「设置」里检查 API Key。";
   else if(status==402) friendly="账户余额不足，暂时只能先用离线对白陪你。";
   else if(status==404) friendly="接口地址似乎不对，请检查「接口地址」。";
   else if(status==429) friendly="请求有点频繁，等几秒再发一次就好。";
   else if(status==400) friendly="这次请求被拒绝了。";
   else if(status>=500) friendly="DeepSeek 服务暂时不太稳定，稍后再试。";
   else friendly="这次没有接通（HTTP "+status+"）。";
   if(status==400&&!string.IsNullOrEmpty(detail)) friendly=friendly+" "+Trim(detail,160);
   return ChatOutcome.Fail(friendly,detail,status,seconds);
  }
  public static ChatOutcome ParseResponse(string json,double seconds) {
   if(string.IsNullOrEmpty(json)) return ChatOutcome.Fail("没有收到回复，再试一次？","empty body",0,seconds);
   ChatResponse response;
   try {response=Deserialize<ChatResponse>(json);}
   catch(Exception e) {return ChatOutcome.Fail("收到的回复格式不对，再试一次？","parse: "+e.Message,0,seconds);}
   if(response==null) return ChatOutcome.Fail("没有收到回复，再试一次？","null response",0,seconds);
   if(response.error!=null) {
    string detail=response.error.message;
    return ChatOutcome.Fail("接口返回了错误，稍后再试一次。",string.IsNullOrEmpty(detail)?"api error":detail,0,seconds);
   }
   if(response.choices==null||response.choices.Length==0||response.choices[0]==null||response.choices[0].message==null) return ChatOutcome.Fail("这次没有收到回复，再试一次？","empty choices",200,seconds);
   string content=response.choices[0].message.content;
   string finish=response.choices[0].finish_reason;
   if(string.IsNullOrEmpty(content)||content.Trim().Length==0) {
    if(finish=="length") return ChatOutcome.Fail("回复被截断了，再问我一次好吗？","empty content, finish=length",200,seconds);
    if(finish=="insufficient_system_resource") return ChatOutcome.Fail("服务那边资源紧张，等一会儿再试。","insufficient_system_resource",200,seconds);
    return ChatOutcome.Fail("这次没有收到文字回复，再试一次？","empty content, finish="+finish,200,seconds);
   }
   content=content.Trim();
   if(content.Length>4000) content=content.Substring(0,4000)+"…";
   var outcome=new ChatOutcome {Ok=true,Text=content,Status=200,Seconds=seconds};
   if(response.usage!=null) {outcome.PromptTokens=response.usage.prompt_tokens;outcome.CompletionTokens=response.usage.completion_tokens;}
   return outcome;
  }
  static string ReadBody(WebResponse response) {
   if(response==null) return "";
   try {
    using(Stream stream=response.GetResponseStream()) {
     if(stream==null) return "";
     using(var reader=new StreamReader(stream,Encoding.UTF8)) return reader.ReadToEnd();
    }
   } catch {return "";}
  }
  static string Serialize(object value) {
   using(var stream=new MemoryStream()) {
    new DataContractJsonSerializer(value.GetType()).WriteObject(stream,value);
    return Encoding.UTF8.GetString(stream.ToArray());
   }
  }
  static T Deserialize<T>(string json) where T:class {
   using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json??""))) {
    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
   }
  }
  static string Trim(string text,int max) {
   text=(text??"").Replace("\r"," ").Replace("\n"," ").Trim();
   return text.Length<=max?text:text.Substring(0,max)+"…";
  }
  static void LogFailure(ChatOutcome outcome) {
   try {
    Directory.CreateDirectory(Storage.Folder);
    File.AppendAllText(Path.Combine(Storage.Folder,"error.log"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" [DeepSeek] HTTP "+outcome.Status+" "+Trim(outcome.Detail,300)+Environment.NewLine);
   } catch {}
  }
 }
 [DataContract] sealed class WireMessage {
  [DataMember(Order=0)] public string role;
  [DataMember(Order=1)] public string content;
 }
 [DataContract] sealed class ThinkingOption {
  [DataMember(Order=0)] public string type;
 }
 [DataContract] sealed class ChatRequest {
  [DataMember(Order=0)] public string model;
  [DataMember(Order=1)] public WireMessage[] messages;
  [DataMember(Order=2)] public int max_tokens;
  [DataMember(Order=3,EmitDefaultValue=false)] public double temperature;
  [DataMember(Order=4,EmitDefaultValue=false)] public ThinkingOption thinking;
  [DataMember(Order=5,EmitDefaultValue=false)] public string reasoning_effort;
 }
 [DataContract] sealed class ChatResponse {
  [DataMember] public string model;
  [DataMember] public ChatChoice[] choices;
  [DataMember] public ChatError error;
  [DataMember] public ChatUsage usage;
 }
 [DataContract] sealed class ChatChoice {
  [DataMember] public ChatResponseMessage message;
  [DataMember] public string finish_reason;
 }
 [DataContract] sealed class ChatResponseMessage {
  [DataMember] public string role;
  [DataMember] public string content;
  [DataMember] public string reasoning_content;
 }
 [DataContract] sealed class ChatError {
  [DataMember] public string message;
  [DataMember] public string type;
 }
 [DataContract] sealed class ChatUsage {
  [DataMember] public int prompt_tokens;
  [DataMember] public int completion_tokens;
  [DataMember] public int total_tokens;
 }
}
