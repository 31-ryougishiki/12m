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
  public string Model = "", FinishReason = "";
  public bool Truncated;
  public bool HasReasoning;
  public int ReasoningTokens;
  public static ChatOutcome Fail(string error,string detail,int status,double seconds) {
   return new ChatOutcome {Ok=false,Error=error??"没有接通。",Detail=detail??"",Status=status,Seconds=seconds};
  }
 }
 public static class DeepSeek {
  public const string DefaultBase = "https://api.deepseek.com";
  public const string DefaultModel = "deepseek-flash";
  public const double DefaultTemperature = 0.7;
  static DeepSeek() {
   try {ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12|SecurityProtocolType.Tls11;ServicePointManager.Expect100Continue=false;} catch {}
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
  public static ChatOutcome Complete(string apiKey,string baseUrl,string model,bool thinking,ChatTurn[] turns,int timeoutSeconds) {
   return Complete(apiKey,baseUrl,model,thinking,turns,timeoutSeconds,thinking?2048:600,DefaultTemperature);
  }
  public static ChatOutcome Complete(string apiKey,string baseUrl,string model,bool thinking,ChatTurn[] turns,int timeoutSeconds,int maxTokens,double temperature,string reasoningEffort="low",int maxResponseChars=4000) {
   var watch=Stopwatch.StartNew();
   string key=ApiKey.Clean(apiKey);
   if(!ApiKey.Valid(key)) return ChatOutcome.Fail("还没有配置 API Key。打开「设置」填入密钥，就能开始在线对话。","missing key",0,0);
   if(turns==null||turns.Length==0) return ChatOutcome.Fail("没有可发送的内容。","empty request",0,0);
   if(!ValidBaseUrl(baseUrl)) return ChatOutcome.Fail("接口地址不完整，请检查「接口地址」。","invalid base url",0,0);
   string json;
   try {json=Serialize(BuildRequest(model,thinking,maxTokens,temperature,turns,reasoningEffort));}
   catch(Exception e) {return ChatOutcome.Fail("准备对话内容时出错了。","serialize: "+e.Message,0,watch.Elapsed.TotalSeconds);}
   try {
    HttpWebRequest request=CreateRequest(Endpoint(baseUrl),key,timeoutSeconds);
    byte[] payload=Encoding.UTF8.GetBytes(json);
    request.ContentLength=payload.Length;
    using(Stream stream=request.GetRequestStream()) stream.Write(payload,0,payload.Length);
    using(HttpWebResponse response=(HttpWebResponse)request.GetResponse()) {
     ChatOutcome outcome=ParseResponse(ReadBody(response),watch.Elapsed.TotalSeconds,maxResponseChars);
     if(outcome.Ok) outcome.Status=(int)response.StatusCode;
     else {outcome.Detail=Redact(outcome.Detail,key);outcome.Status=outcome.Status==0?(int)response.StatusCode:outcome.Status;LogFailure(outcome);}
     return outcome;
    }
   } catch(WebException e) {
    ChatOutcome outcome=FailFromWeb(e,key,watch.Elapsed.TotalSeconds);
    LogFailure(outcome);
    return outcome;
   } catch(Exception e) {
    ChatOutcome outcome=ChatOutcome.Fail("连接时出了点问题，稍后再试一次？","exception: "+Redact(e.Message,key),0,watch.Elapsed.TotalSeconds);
    LogFailure(outcome);
    return outcome;
   }
  }
  static ChatRequest BuildRequest(string model,bool thinking,int maxTokens,double temperature,ChatTurn[] turns,string reasoningEffort) {
   var request=new ChatRequest();
   request.model=string.IsNullOrWhiteSpace(model)?DefaultModel:model.Trim();
   request.max_tokens=Math.Max(32,Math.Min(16384,maxTokens));
   request.temperature=Math.Max(0,Math.Min(2,temperature));
   request.thinking=new ThinkingOption();
   if(thinking) {request.thinking.type="enabled";request.reasoning_effort=reasoningEffort=="high"?"high":"low";}
   else request.thinking.type="disabled";
   request.messages=new WireMessage[turns.Length];
   for(int i=0;i<turns.Length;i++) {
    WireMessage message=new WireMessage();
    message.role=turns[i].Role;
    message.content=turns[i].Content;
    request.messages[i]=message;
   }
   return request;
  }
  static HttpWebRequest CreateRequest(string url,string key,int timeoutSeconds) {
   var request=(HttpWebRequest)WebRequest.Create(url);
   request.Method="POST";
   request.ContentType="application/json; charset=utf-8";
   request.Accept="application/json";
   request.UserAgent=AppVersion.UserAgent;
   request.Timeout=Math.Max(5,timeoutSeconds)*1000;
   request.ReadWriteTimeout=Math.Max(5,timeoutSeconds)*1000;
   request.KeepAlive=false;
   request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
   request.Headers[HttpRequestHeader.Authorization]="Bearer "+key;
   return request;
  }
  static ChatOutcome FailFromWeb(WebException e,string key,double seconds) {
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
   return FromStatus(status,Redact(detail,key),seconds,e.Status);
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
  public static ChatOutcome ParseResponse(string json,double seconds,int maxResponseChars=4000) {
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
   int limit=Math.Max(4000,Math.Min(24000,maxResponseChars));
   bool clipped=content.Length>limit;
   if(clipped) content=content.Substring(0,limit)+"…";
   var outcome=new ChatOutcome {Ok=true,Text=content,Status=200,Seconds=seconds,Model=response.model??"",FinishReason=finish??"",Truncated=finish=="length"||clipped};
   outcome.HasReasoning=!string.IsNullOrWhiteSpace(response.choices[0].message.reasoning_content);
   if(response.usage!=null) {outcome.PromptTokens=response.usage.prompt_tokens;outcome.CompletionTokens=response.usage.completion_tokens;if(response.usage.completion_tokens_details!=null)outcome.ReasoningTokens=response.usage.completion_tokens_details.reasoning_tokens;}
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
  static string Redact(string text,string key) {
   if(string.IsNullOrEmpty(text)||string.IsNullOrEmpty(key)) return text??"";
   return text.Replace(key,ApiKey.Mask(key));
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
  [DataMember] public ChatCompletionDetails completion_tokens_details;
 }
 [DataContract] sealed class ChatCompletionDetails { [DataMember] public int reasoning_tokens; }
 [DataContract] public sealed class ChatStore {
  [DataMember] public ChatStoredTurn[] turns;
 }
 [DataContract] public sealed class ChatStoredTurn {
  [DataMember] public string role;
  [DataMember] public string content;
 }
}
