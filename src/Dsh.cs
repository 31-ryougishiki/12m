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
 /// <summary>DSH 桥接插件写在数据目录里的握手信息；桌宠只读它，不接触 DSH 进程内部。</summary>
 [DataContract] public sealed class DshHandshake {
  [DataMember] public int version;
  [DataMember] public string host;
  [DataMember] public int port;
  [DataMember] public string token;
  [DataMember] public int pid;
  [DataMember] public string startedAt;
  [DataMember] public string workspace;
  [DataMember] public string webUrl;
  [DataMember] public int maxActive;
 }
 /// <summary>桌宠工作台里的一个 DSH 任务；状态与桥接插件的 jobView 对应。</summary>
 public sealed class DshJob {
  public string Id = "";
  public string Title = "";
  public string Status = "idle";
  public string Task = "";
  public string Cwd = "";
  public string Permission = "";
  public string SessionId = "";
  public string Text = "";
  public string Progress = "";
  public string Error = "";
  public string Note = "";
  public int Tools;
  public double Seconds;
  public DateTime UpdatedAt = DateTime.Now;
  public bool Busy {
   get {return Status=="starting"||Status=="queued"||Status=="running"||Status=="cancelling";}
  }
  public bool Finished {
   get {return Status=="done"||Status=="error"||Status=="cancelled";}
  }
  public string PermissionLabel {
   get {
    if(Permission=="read-only") return "只读";
    if(Permission=="danger-full-access") return "完整访问";
    return "工作区可写";
   }
  }
 }
 /// <summary>任务提案的标记解析；模型输出里只保留给人看的部分。</summary>
 public static class DshTask {
  public const string StartMarker="[[DSH_TASK]]";
  public const string EndMarker="[[/DSH_TASK]]";
  /// <summary>去掉回复里的任务块，返回任务文本；没有标记时回复保持原样。</summary>
  public static string Extract(ref string reply) {
   string text=reply??"";
   int start=text.IndexOf(StartMarker,StringComparison.OrdinalIgnoreCase);
   if(start<0) {reply=text.Trim();return "";}
   int bodyStart=start+StartMarker.Length;
   int valueEnd,after;
   bool closed=TagMarkers.FindClose(text,bodyStart,"DSH_TASK",out valueEnd,out after);
   string task=closed?text.Substring(bodyStart,valueEnd-bodyStart):text.Substring(bodyStart);
   string cleaned=closed?text.Substring(0,start)+text.Substring(after):text.Substring(0,start);
   reply=cleaned.Trim();
   task=task.Trim();
   if(task.Length>4000) task=task.Substring(0,4000)+"…";
   return task;
  }
  public static string Clip(string text,int max) {
   text=(text??"").Trim();
   return text.Length<=max?text:text.Substring(0,max)+"…";
  }
 }
 /// <summary>桌宠侧 DSH 工作台：读握手文件、提交任务、轮询结果、取消。</summary>
 /// <summary>本机 DSH 桥接的 HTTP 客户端；只连 127.0.0.1，带握手里的随机 token。</summary>
 public static class DshApi {
  public const string HandshakeFile="dsh-bridge.json";
  static readonly object Gate=new object();
  public static string HandshakePath {get {return Path.Combine(Storage.Folder,HandshakeFile);}}
  public static DshHandshake ReadHandshake(out string error) {
   error="";
   DshHandshake value=JsonFile.Load<DshHandshake>(HandshakePath,out error);
   if(value==null) {if(error.Length==0)error="没有找到 dsh-bridge.json";return null;}
   if(value.port<=0||value.port>65535) {error="握手文件端口无效";return null;}
   if(string.IsNullOrEmpty(value.token)||value.token.Length<16) {error="握手文件 token 无效";return null;}
   if(string.IsNullOrEmpty(value.workspace)) value.workspace=Storage.Folder;
   return value;
  }
  public static string PromptContext(PetState state) {
   if(state==null||!state.DshEnabled) return "DSH 委托已在设置里关闭。";
   string error;var handshake=ReadHandshake(out error);
   if(handshake==null) return "现在没有检测到 DSH 工作台（"+error+"）。如果用户要求实际动手，先说明需要打开 DSH。";
   return "DSH 工作台在线，工作区："+handshake.workspace+"。";
  }
  public static string Clip(string text,int max) {
   text=(text??"").Trim();
   return text.Length<=max?text:text.Substring(0,max)+"…";
  }
  public static DshJob Start(string task,string permission,string title) {
   string error;var handshake=ReadHandshake(out error);
   if(handshake==null) return ErrorJob("DSH 桥接没有运行："+error,task,permission);
   var request=new DshTaskRequest {prompt=task,permission=permission,cwd=handshake.workspace,title=title??""};
   string body;
   try {body=Serialize(request);} catch(Exception e) {return ErrorJob("准备 DSH 任务失败："+e.Message,task,permission);}
   string response;int status;string requestError;
   if(!Send("POST",BaseUrl(handshake)+"/task",handshake.token,body,30000,out response,out status,out requestError)) {
    return ErrorJob(Describe(status,requestError),task,permission);
   }
   DshTaskResponse created;
   try {created=Deserialize<DshTaskResponse>(response);} catch(Exception e) {return ErrorJob("DSH 返回格式不对："+e.Message,task,permission);}
   if(created==null||string.IsNullOrEmpty(created.jobId)) return ErrorJob("DSH 没有返回任务 id",task,permission);
   return new DshJob {Id=created.jobId,SessionId=created.sessionId??"",Title=created.title??title??"",Status=created.status??"queued",Task=task,Permission=permission,Cwd=handshake.workspace,UpdatedAt=DateTime.Now};
  }
  public static DshJob Poll(string jobId) {
   string error;var handshake=ReadHandshake(out error);
   if(handshake==null) return ErrorJob("DSH 桥接已断开："+error,"","");
   string response;int status;string requestError;
   if(!Send("GET",BaseUrl(handshake)+"/task/"+Uri.EscapeDataString(jobId),handshake.token,null,15000,out response,out status,out requestError)) {
    return ErrorJob(Describe(status,requestError),"","");
   }
   DshJobResponse value;
   try {value=Deserialize<DshJobResponse>(response);} catch(Exception e) {return ErrorJob("DSH 状态格式不对："+e.Message,"","");}
   if(value==null) return ErrorJob("DSH 没有返回任务状态","","");
   return new DshJob {
    Id=value.id??jobId,Title=value.title??"",Status=value.status??"error",Task=value.task??"",Cwd=value.cwd??"",
    Permission=value.permission??"",SessionId=value.sessionId??"",Text=value.text??"",Progress=value.progress??"",
    Error=value.error??"",Note=value.note??"",Tools=value.tools,Seconds=value.seconds,UpdatedAt=DateTime.Now
   };
  }
  public static bool Cancel(string jobId) {
   string error;var handshake=ReadHandshake(out error);
   if(handshake==null) return false;
   string response;int status;string requestError;
   return Send("POST",BaseUrl(handshake)+"/task/"+Uri.EscapeDataString(jobId)+"/cancel",handshake.token,"{}",15000,out response,out status,out requestError);
  }
  public static DshHealthResult Health() {
   string error;var handshake=ReadHandshake(out error);
   if(handshake==null) return new DshHealthResult {Ok=false,Error="桥接文件不可用："+error};
   string response;int status;string requestError;
   if(!Send("GET",BaseUrl(handshake)+"/health",handshake.token,null,5000,out response,out status,out requestError)) {
    return new DshHealthResult {Ok=false,Error=Describe(status,requestError)};
   }
   try {
    var health=Deserialize<DshHealthResponse>(response);
    if(health==null||!health.ok) return new DshHealthResult {Ok=false,Error="DSH 桥接没有正常响应"};
    return new DshHealthResult {Ok=true,Workspace=health.workspace??handshake.workspace,WebUrl=health.webUrl??handshake.webUrl};
   } catch(Exception e) {return new DshHealthResult {Ok=false,Error="DSH 健康检查格式不对："+e.Message};}
  }
  static DshJob ErrorJob(string error,string task,string permission) {
   return new DshJob {Status="error",Error=error??"",Task=task??"",Permission=permission??"",UpdatedAt=DateTime.Now};
  }
  static string BaseUrl(DshHandshake handshake) {return "http://127.0.0.1:"+handshake.port;}
  static string Describe(int status,string error) {
   if(status==409) return "DSH 工作台正忙，等当前任务结束再试。";
   if(status==401) return "DSH 桥接 token 校验失败，重启 DSH 后再试。";
   if(status==403) return "这个目录不在 DSH 工作区内。";
   if(status==404) return "DSH 任务不存在或桥接已重启。";
   if(status==0) return "连不上 DSH 桥接："+(string.IsNullOrEmpty(error)?"网络错误":error);
   return "DSH 请求失败（HTTP "+status+"）："+(string.IsNullOrEmpty(error)?"未知错误":error);
  }
  static bool Send(string method,string url,string token,string body,int timeoutMs,out string response,out int status,out string error) {
   response="";status=0;error="";
   try {
    var request=(HttpWebRequest)WebRequest.Create(url);
    request.Method=method;request.Timeout=timeoutMs;request.ReadWriteTimeout=timeoutMs;request.KeepAlive=false;
    request.Accept="application/json";
    request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;
    if(body!=null) {
     byte[] bytes=Encoding.UTF8.GetBytes(body);
     request.ContentType="application/json; charset=utf-8";request.ContentLength=bytes.Length;
     using(Stream stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
    }
    using(var web=(HttpWebResponse)request.GetResponse()) {
     status=(int)web.StatusCode;response=ReadBody(web);return true;
    }
   } catch(WebException e) {
    var web=e.Response as HttpWebResponse;
    status=web==null?0:(int)web.StatusCode;
    try {response=ReadBody(web);} catch {}
    error=ExtractError(response,e.Message);
    return false;
   } catch(Exception e) {
    error=e.Message;return false;
   }
  }
  static string ExtractError(string body,string fallback) {
   if(!string.IsNullOrEmpty(body)) {
    try {var value=Deserialize<DshErrorResponse>(body);if(value!=null&&!string.IsNullOrEmpty(value.error))return value.error;} catch {}
   }
   return fallback??"";
  }
  static string ReadBody(WebResponse response) {
   if(response==null)return "";
   try {using(Stream stream=response.GetResponseStream()) {if(stream==null)return "";using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd();}} catch {return "";}
  }
  static string Serialize(object value) {
   lock(Gate) {
    using(var stream=new MemoryStream()) {
     new DataContractJsonSerializer(value.GetType()).WriteObject(stream,value);
     return Encoding.UTF8.GetString(stream.ToArray());
    }
   }
  }
  static T Deserialize<T>(string json) where T:class {
   lock(Gate) {
    using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json??""))) {
     return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
    }
   }
  }
 }
 public sealed class DshHealthResult {
  public bool Ok;
  public string Error="";
  public string Workspace="";
  public string WebUrl="";
 }
 [DataContract] sealed class DshTaskRequest {
  [DataMember] public string prompt;
  [DataMember] public string permission;
  [DataMember] public string cwd;
  [DataMember(EmitDefaultValue=false)] public string title;
 }
 [DataContract] sealed class DshTaskResponse {
  [DataMember] public string jobId;
  [DataMember] public string sessionId;
  [DataMember] public string title;
  [DataMember] public string status;
  [DataMember] public string error;
 }
 [DataContract] sealed class DshJobResponse {
  [DataMember] public string id;
  [DataMember] public string title;
  [DataMember] public string status;
  [DataMember] public string task;
  [DataMember] public string cwd;
  [DataMember] public string permission;
  [DataMember] public string sessionId;
  [DataMember] public string text;
  [DataMember] public string progress;
  [DataMember] public string error;
  [DataMember] public string note;
  [DataMember] public int tools;
  [DataMember] public double seconds;
 }
 [DataContract] sealed class DshHealthResponse {
  [DataMember] public bool ok;
  [DataMember] public string workspace;
  [DataMember] public string webUrl;
 }
 [DataContract] sealed class DshErrorResponse {
  [DataMember] public string error;
 }
}
