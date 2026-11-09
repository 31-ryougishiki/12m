using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

#pragma warning disable 0649
namespace ElymsPet {
 public sealed class BiliVideo {
  public string Bvid="", Title="", Author="", Description="", Cover="", Tag="";
  public long Aid, Cid, Play, Like, Danmaku, Reply, Favorite, Coin, Share;
  public int Duration;
  public DateTime PubDate=DateTime.MinValue;
  public string Url {get {return string.IsNullOrEmpty(Bvid)?"https://www.bilibili.com/":"https://www.bilibili.com/video/"+Bvid;}}
  public string DurationText {get {return BiliApi.FormatDuration(Duration);}}
  public string PlayText {get {return BiliApi.FormatCount(Play);}}
  public string DateText {get {return PubDate==DateTime.MinValue?"":PubDate.ToString("yyyy-MM-dd");}}
 }
 public sealed class BiliComment {
  public string Author="", Message="";
  public long Like;
  public DateTime Time=DateTime.MinValue;
  public string TimeText {get {return Time==DateTime.MinValue?"":Time.ToString("MM-dd HH:mm");}}
 }
 public sealed class BiliBrowseResult {
  public bool Ok;
  public string Error="", Note="", Topic="", Summary="";
  public BiliVideo[] Videos=new BiliVideo[0];
  public BiliVideo Selected;
  public BiliComment[] Comments=new BiliComment[0];
  public double Seconds;
  public bool FromChat;
 }
 public sealed class BiliException : Exception {
  public int Code;
  public int Status;
  public BiliException(string message,int code,int status) : base(message) {Code=code;Status=status;}
 }
 public sealed class BiliAccountInfo {
  public bool LoggedIn;
  public string Name="", Mid="", Error="";
 }
 public sealed class BiliCommentResult {
  public bool Ok;
  public long Rpid;
  public string Error="";
 }
 /// <summary>
 /// 只读 B 站公开内容客户端。WBI 签名、搜索、视频详情和评论的接口路径参考了
 /// OpenBiliClaw（MIT，https://github.com/whiteguo233/OpenBiliClaw）的
 /// src/openbiliclaw/bilibili/api.py，并按本桌宠的 .NET Framework 环境重写。
 /// </summary>
 public static class BiliApi {
  const string BaseUrl="https://api.bilibili.com";
  const string UserAgent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
  static readonly object Gate=new object();
  static string cookie="";
  static DateTime cookieAt=DateTime.MinValue;
  static string imgKey="",subKey="";
  static DateTime wbiAt=DateTime.MinValue;
  static DateTime lastRequest=DateTime.MinValue;
  static DateTime lastCommentAt=DateTime.MinValue;
  static string userCookie="";
  static readonly int[] WbiTab={46,47,18,2,53,8,23,32,15,50,10,31,58,3,45,35,27,43,5,49,33,9,42,19,29,28,14,39,12,38,41,13,37,48,7,16,24,55,40,61,26,17,0,1,60,51,30,4,22,25,54,21,56,59,6,63,57,62,11,36,20,34,44,52};
  static BiliApi() {
   try {ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12|SecurityProtocolType.Tls11;} catch {}
  }
  public static string FormatCount(long value) {
   if(value>=100000000) return (value/100000000.0).ToString("0.#")+"亿";
   if(value>=10000) return (value/10000.0).ToString("0.#")+"万";
   return value.ToString();
  }
  public static string FormatDuration(int seconds) {
   if(seconds<=0) return "";
   int h=seconds/3600,m=(seconds%3600)/60,s=seconds%60;
   if(h>0) return h+":"+m.ToString("00")+":"+s.ToString("00");
   return m+":"+s.ToString("00");
  }
  public static string StripHtml(string text) {
   text=WebUtility.HtmlDecode(text??"");
   text=Regex.Replace(text,"<[^>]+>","");
   return text.Replace("\r"," ").Replace("\n"," ").Trim();
  }
  public static bool ValidBvid(string bvid) {
   return !string.IsNullOrEmpty(bvid)&&Regex.IsMatch(bvid,"^BV[0-9A-Za-z]{10}$");
  }
  public static BiliVideo[] Search(string keyword,int count,int timeoutSeconds) {
   lock(Gate) {
    try {return SearchOnce(keyword,count,timeoutSeconds);}
    catch(BiliException e) {
     if(e.Status==412||e.Code==-412) {ResetSession();return SearchOnce(keyword,count,timeoutSeconds);}
     throw;
    }
   }
  }
  static BiliVideo[] SearchOnce(string keyword,int count,int timeoutSeconds) {
   keyword=(keyword??"").Trim();
   if(keyword.Length==0) throw new BiliException("搜索词是空的。",0,0);
   if(keyword.Length>100) keyword=keyword.Substring(0,100);
   EnsureCookie(timeoutSeconds);
   EnsureWbi(timeoutSeconds);
   var p=new SortedDictionary<string,string>(StringComparer.Ordinal);
   p["keyword"]=keyword;p["search_type"]="video";p["page"]="1";p["page_size"]=Math.Max(1,Math.Min(20,count)).ToString();p["order"]="totalrank";p["web_location"]="1430654";
   string query=SignParams(p,imgKey,subKey,NowUnix());
   string referer="https://search.bilibili.com/all?keyword="+Uri.EscapeDataString(keyword);
   string body=GetRaw(BaseUrl+"/x/web-interface/wbi/search/type?"+query,referer,"https://search.bilibili.com",timeoutSeconds,true);
   BiliSearchEnvelope env=Deserialize<BiliSearchEnvelope>(body);
   if(env==null) throw new BiliException("B站搜索没有返回内容。",0,200);
   if(env.code!=0) throw new BiliException(ApiMessage(env.code,env.message),env.code,200);
   var list=new List<BiliVideo>();
   if(env.data!=null&&env.data.result!=null) {
    for(int i=0;i<env.data.result.Length&&list.Count<count;i++) {
     BiliSearchItem item=env.data.result[i];
     if(item==null||string.IsNullOrEmpty(item.bvid)||!ValidBvid(item.bvid)) continue;
     list.Add(FromSearch(item));
    }
   }
   return list.ToArray();
  }
  public static BiliVideo[] Ranking(int count,int timeoutSeconds) {
   lock(Gate) {
    EnsureCookie(timeoutSeconds);
    string body=GetRaw(BaseUrl+"/x/web-interface/ranking/v2?rid=0&type=all","https://www.bilibili.com/","",timeoutSeconds,true);
    BiliRankEnvelope env=Deserialize<BiliRankEnvelope>(body);
    if(env==null||env.code!=0) throw new BiliException(env==null?"B站热门没有返回内容。":ApiMessage(env.code,env.message),env==null?0:env.code,200);
    var list=new List<BiliVideo>();
    if(env.data!=null&&env.data.list!=null) {
     for(int i=0;i<env.data.list.Length&&list.Count<count;i++) {
      BiliRankItem item=env.data.list[i];
      if(item==null||string.IsNullOrEmpty(item.bvid)) continue;
      list.Add(FromRank(item));
     }
    }
    return list.ToArray();
   }
  }
  public static BiliVideo LoadVideo(string bvid,int timeoutSeconds) {
   if(!ValidBvid(bvid)) throw new BiliException("这个 BV 号看起来不完整。",0,0);
   lock(Gate) {
    EnsureCookie(timeoutSeconds);
    EnsureWbi(timeoutSeconds);
    var p=new SortedDictionary<string,string>(StringComparer.Ordinal);
    p["bvid"]=bvid;
    string query=SignParams(p,imgKey,subKey,NowUnix());
    string body=GetRaw(BaseUrl+"/x/web-interface/wbi/view?"+query,"https://www.bilibili.com/video/"+bvid,"",timeoutSeconds,true);
    BiliViewEnvelope env=Deserialize<BiliViewEnvelope>(body);
    if(env==null||env.data==null||env.code!=0) throw new BiliException(env==null?"B站没有返回视频详情。":ApiMessage(env.code,env.message),env==null?0:env.code,200);
    return FromView(env.data);
   }
  }
  public static BiliComment[] Comments(BiliVideo video,int count,int timeoutSeconds) {
   if(video==null||video.Aid<=0) return new BiliComment[0];
   lock(Gate) {
    EnsureCookie(timeoutSeconds);
    string url=BaseUrl+"/x/v2/reply/main?oid="+video.Aid+"&type=1&mode=3&ps="+Math.Max(1,Math.Min(20,count))+"&next=0";
    string body=GetRaw(url,"https://www.bilibili.com/video/"+video.Bvid,"",timeoutSeconds,true);
    BiliReplyEnvelope env=Deserialize<BiliReplyEnvelope>(body);
    if(env==null||env.code!=0) throw new BiliException(env==null?"B站没有返回评论。":ApiMessage(env.code,env.message),env==null?0:env.code,200);
    var list=new List<BiliComment>();
    if(env.data!=null&&env.data.replies!=null) {
     for(int i=0;i<env.data.replies.Length&&list.Count<count;i++) {
      BiliReply reply=env.data.replies[i];
      if(reply==null||reply.content==null||string.IsNullOrEmpty(reply.content.message)) continue;
      list.Add(new BiliComment {
       Author=reply.member==null?"":(reply.member.uname??""),
       Message=StripHtml(reply.content.message),
       Like=reply.like,
       Time=FromUnix(reply.ctime)
      });
     }
    }
    return list.ToArray();
   }
  }
  static BiliVideo FromSearch(BiliSearchItem item) {
   int duration;ParseDuration(item.duration,out duration);
   return new BiliVideo {
    Bvid=item.bvid??"",Aid=item.aid,Title=StripHtml(item.title),Author=StripHtml(item.author),
    Description=StripHtml(item.description),Cover=FixUrl(item.pic),Tag=StripHtml(item.tag),
    Play=item.play,Danmaku=item.video_review,Favorite=item.favorites,Duration=duration,
    PubDate=FromUnix(item.pubdate)
   };
  }
  static BiliVideo FromView(BiliViewData data) {
   var stat=data.stat??new BiliViewStat();
   var owner=data.owner??new BiliViewOwner();
   return new BiliVideo {
    Bvid=data.bvid??"",Aid=data.aid,Cid=data.cid,Title=StripHtml(data.title),Author=StripHtml(owner.name),
    Description=StripHtml(data.desc),Cover=FixUrl(data.pic),Duration=data.duration,
    PubDate=FromUnix(data.pubdate),Play=stat.view,Danmaku=stat.danmaku,Reply=stat.reply,
    Favorite=stat.favorite,Coin=stat.coin,Share=stat.share,Like=stat.like
   };
  }
  static BiliVideo FromRank(BiliRankItem item) {
   var stat=item.stat??new BiliViewStat();
   var owner=item.owner??new BiliViewOwner();
   return new BiliVideo {
    Bvid=item.bvid??"",Aid=item.aid,Cid=item.cid,Title=StripHtml(item.title),Author=StripHtml(owner.name),
    Description=StripHtml(item.desc),Cover=FixUrl(item.pic),Duration=item.duration,
    PubDate=FromUnix(item.pubdate),Play=stat.view,Danmaku=stat.danmaku,Reply=stat.reply,
    Favorite=stat.favorite,Coin=stat.coin,Share=stat.share,Like=stat.like
   };
  }
  static void ParseDuration(string value,out int seconds) {
   seconds=0;
   if(string.IsNullOrEmpty(value)) return;
   string[] parts=value.Split(':');
   int total=0;
   for(int i=0;i<parts.Length;i++) {
    int n;if(!int.TryParse(parts[i],out n)) return;
    total=total*60+Math.Max(0,n);
   }
   seconds=total;
  }
  static string FixUrl(string url) {
   url=(url??"").Trim();
   if(url.StartsWith("//")) return "https:"+url;
   return url;
  }
  static DateTime FromUnix(long value) {
   if(value<=0) return DateTime.MinValue;
   try {return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(value).ToLocalTime();} catch {return DateTime.MinValue;}
  }
  static long NowUnix() {return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;}
  static string ApiMessage(int code,string message) {
   if(code==-412) return "B站现在要求验证一下，稍后再试。";
   if(code==-352) return "B站风控拦住了这次请求，等一会儿再逛。";
   if(code==-101) return "这次需要登录才能继续，桌宠只逛公开内容。";
   if(code==-404) return "这个视频不存在或已经下架。";
   string text=StripHtml(message);
   return text.Length>0?"B站返回了错误（"+code+"）："+text:"B站返回了错误（"+code+"）。";
  }
  public static bool HasLogin {get {return userCookie.Length>0;}}
  public static void SetUserCookie(string value) {userCookie=(value??"").Trim();}
  public static void ClearUserCookie() {userCookie="";}
  public static string AnonymousCookie() {
   lock(Gate) {EnsureCookie(20);return cookie;}
  }
  public static string CsrfToken() {
   return CookieValue(userCookie,"bili_jct");
  }
  public static string CookieValue(string header,string name) {
   string[] parts=(header??"").Split(';');
   for(int i=0;i<parts.Length;i++) {
    string part=parts[i].Trim();
    int eq=part.IndexOf('=');
    if(eq>0&&part.Substring(0,eq)==name) return part.Substring(eq+1);
   }
   return "";
  }
  public static BiliAccountInfo GetAccount(int timeoutSeconds) {
   lock(Gate) {
    try {
     EnsureCookie(timeoutSeconds);
     string body=GetRaw(BaseUrl+"/x/web-interface/nav","https://www.bilibili.com/","",timeoutSeconds,true);
     BiliNavEnvelope env=Deserialize<BiliNavEnvelope>(body);
     if(env==null||env.data==null) return new BiliAccountInfo {Error="B站没有返回账号信息。"};
     return new BiliAccountInfo {
      LoggedIn=env.data.isLogin,
      Name=env.data.uname??"",
      Mid=env.data.mid.ToString(),
      Error=env.data.isLogin?"":"账号未登录或登录已过期。"
     };
    } catch(Exception e) {
     return new BiliAccountInfo {Error=e.Message};
    }
   }
  }
  public static BiliCommentResult PostComment(BiliVideo video,string message,string csrf,int timeoutSeconds) {
   message=(message??"").Trim();
   if(video==null||video.Aid<=0) throw new BiliException("没有选中视频。",0,0);
   if(message.Length==0) throw new BiliException("评论内容不能为空。",0,0);
   if(message.Length>1000) throw new BiliException("评论内容最多 1000 字。",0,0);
   if(string.IsNullOrEmpty(csrf)) throw new BiliException("需要先扫码登录。",-101,0);
   lock(Gate) {
    int wait=(int)(10000-(DateTime.UtcNow-lastCommentAt).TotalMilliseconds);
    if(wait>0) throw new BiliException("刚发过一条，等 "+(wait/1000+1)+" 秒再发。",0,0);
    EnsureCookie(timeoutSeconds);
    string body="oid="+video.Aid+"&type=1&message="+UrlEncode(message)+"&csrf="+UrlEncode(csrf);
    string response=PostForm(BaseUrl+"/x/v2/reply/add",body,video.Url,timeoutSeconds);
    BiliCommentAddEnvelope env=Deserialize<BiliCommentAddEnvelope>(response);
    if(env==null) throw new BiliException("B站没有返回评论结果。",0,200);
    if(env.code!=0) throw new BiliException(CommentMessage(env.code,env.message),env.code,200);
    lastCommentAt=DateTime.UtcNow;
    return new BiliCommentResult {Ok=true,Rpid=env.data==null?0:env.data.rpid};
   }
  }
  public static BiliCommentResult DeleteComment(BiliVideo video,long rpid,string csrf,int timeoutSeconds) {
   if(video==null||video.Aid<=0) throw new BiliException("没有选中视频。",0,0);
   if(rpid<=0) throw new BiliException("没有指定要删除的评论。",0,0);
   if(string.IsNullOrEmpty(csrf)) throw new BiliException("需要先扫码登录。",-101,0);
   lock(Gate) {
    EnsureCookie(timeoutSeconds);
    string body="oid="+video.Aid+"&type=1&rpid="+rpid+"&csrf="+UrlEncode(csrf);
    string response=PostForm(BaseUrl+"/x/v2/reply/del",body,"https://www.bilibili.com/video/"+video.Bvid,timeoutSeconds);
    BiliDeleteEnvelope env=Deserialize<BiliDeleteEnvelope>(response);
    if(env==null) throw new BiliException("B站没有返回删评结果。",0,200);
    if(env.code!=0) throw new BiliException(CommentMessage(env.code,env.message),env.code,200);
    return new BiliCommentResult {Ok=true,Rpid=rpid};
   }
  }
  static string CommentMessage(int code,string message) {
   if(code==-101) return "登录已过期，请重新扫码登录。";
   if(code==-111) return "CSRF 校验失败，请重新登录。";
   if(code==-400) return "评论内容不符合要求。";
   if(code==-404) return "评论不存在或已经被删除。";
   if(code==-403) return "没有权限删除这条评论。";
   if(code==-412) return "B站风控拦住了评论，等一会儿再试。";
   if(code==12002) return "评论区已关闭，不能发送。";
   if(code==12051) return "评论发得太频繁了，等一会儿再试。";
   string text=StripHtml(message);
   return text.Length>0?"B站拒绝了评论（"+code+"）："+text:"B站拒绝了评论（"+code+"）。";
  }
  static void EnsureCookie(int timeoutSeconds) {
   if(cookie.Length>0&&(DateTime.UtcNow-cookieAt).TotalMinutes<30) return;
   string body=GetRaw(BaseUrl+"/x/frontend/finger/spi","https://www.bilibili.com/","",timeoutSeconds,false);
   BiliFingerEnvelope env=Deserialize<BiliFingerEnvelope>(body);
   if(env==null||env.data==null||string.IsNullOrEmpty(env.data.b_3)) throw new BiliException("没能拿到 B站的匿名访问标识。",0,200);
   cookie="buvid3="+(env.data.b_3??"")+"; buvid4="+(env.data.b_4??"");
   cookieAt=DateTime.UtcNow;
  }
  static void EnsureWbi(int timeoutSeconds) {
   if(imgKey.Length>0&&subKey.Length>0&&(DateTime.UtcNow-wbiAt).TotalMinutes<5) return;
   string body=GetRaw(BaseUrl+"/x/web-interface/nav","https://www.bilibili.com/","",timeoutSeconds,false);
   BiliNavEnvelope env=Deserialize<BiliNavEnvelope>(body);
   if(env==null||env.data==null||env.data.wbi_img==null) throw new BiliException("没能拿到 B站的 WBI 签名密钥。",0,200);
   imgKey=KeyFromUrl(env.data.wbi_img.img_url);
   subKey=KeyFromUrl(env.data.wbi_img.sub_url);
   if(imgKey.Length==0||subKey.Length==0) throw new BiliException("B站 WBI 密钥为空。",0,200);
   wbiAt=DateTime.UtcNow;
  }
  static string KeyFromUrl(string url) {
   url=(url??"").Trim();
   int slash=url.LastIndexOf('/');
   string name=slash>=0?url.Substring(slash+1):url;
   int dot=name.LastIndexOf('.');
   return dot>0?name.Substring(0,dot):name;
  }
  public static string SignParams(SortedDictionary<string,string> parameters,string imageKey,string subKeyValue,long wts) {
   var signed=new SortedDictionary<string,string>(StringComparer.Ordinal);
   foreach(KeyValuePair<string,string> pair in parameters) signed[pair.Key]=Sanitize(pair.Value);
   signed["wts"]=wts.ToString();
   var query=new StringBuilder();
   foreach(KeyValuePair<string,string> pair in signed) {
    if(query.Length>0)query.Append('&');
    query.Append(UrlEncode(pair.Key)).Append('=').Append(UrlEncode(pair.Value));
   }
   string mixin=BuildMixinKey(imageKey,subKeyValue);
   string wRid=Md5(query.ToString()+mixin);
   query.Append("&w_rid=").Append(wRid);
   return query.ToString();
  }
  static string BuildMixinKey(string imageKey,string subKeyValue) {
   string merged=(imageKey??"")+(subKeyValue??"");
   var text=new StringBuilder();
   for(int i=0;i<WbiTab.Length&&i<merged.Length;i++) text.Append(merged[WbiTab[i]]);
   string key=text.ToString();
   return key.Length>32?key.Substring(0,32):key;
  }
  static string Sanitize(string value) {return Regex.Replace(value??"","[!'()*]","");}
  static string UrlEncode(string value) {
   return Uri.EscapeDataString(value??"").Replace("%20","+").Replace("%7E","~");
  }
  static string Md5(string text) {
   using(var md5=MD5.Create()) {
    byte[] bytes=md5.ComputeHash(Encoding.UTF8.GetBytes(text??""));
    var sb=new StringBuilder();
    for(int i=0;i<bytes.Length;i++) sb.Append(bytes[i].ToString("x2"));
    return sb.ToString();
   }
  }
  static void ResetSession() {cookie="";cookieAt=DateTime.MinValue;imgKey="";subKey="";wbiAt=DateTime.MinValue;}
  static string GetRaw(string url,string referer,string origin,int timeoutSeconds,bool withCookie) {
   int wait=(int)(250-(DateTime.UtcNow-lastRequest).TotalMilliseconds);
   if(wait>0) Thread.Sleep(wait);
   lastRequest=DateTime.UtcNow;
   try {
    var request=(HttpWebRequest)WebRequest.Create(url);
    request.Method="GET";request.Timeout=Math.Max(5,timeoutSeconds)*1000;request.ReadWriteTimeout=Math.Max(5,timeoutSeconds)*1000;
    request.UserAgent=UserAgent;request.Accept="application/json, text/plain, */*";request.KeepAlive=false;
    request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
    request.Referer=referer??"https://www.bilibili.com/";
    if(!string.IsNullOrEmpty(origin)) request.Headers["Origin"]=origin;
    if(withCookie) {
     string header=userCookie.Length>0?(cookie.Length>0?userCookie+"; "+cookie:userCookie):cookie;
     if(header.Length>0) request.Headers[HttpRequestHeader.Cookie]=header;
    }
    using(HttpWebResponse response=(HttpWebResponse)request.GetResponse()) {
     string body=ReadBody(response);
     if(body.Length>0&&body.TrimStart().StartsWith("<",StringComparison.Ordinal)) throw new BiliException("B站返回了风控页面，等一会儿再试。",-412,(int)response.StatusCode);
     return body;
    }
   } catch(BiliException) {throw;}
   catch(WebException e) {
    HttpWebResponse response=e.Response as HttpWebResponse;
    int status=response==null?0:(int)response.StatusCode;
    string body="";try {body=ReadBody(response);} catch {}
    if(status==412||status==429) throw new BiliException("B站现在有点忙，稍后再试。",-412,status);
    string detail=ExtractError(body);
    if(detail.Length==0) detail=e.Message;
    throw new BiliException(detail,-1,status);
   } catch(Exception e) {
    throw new BiliException("连接 B站时出了点问题："+e.Message,-1,0);
   }
  }
  static string PostForm(string url,string body,string referer,int timeoutSeconds) {
   int wait=(int)(250-(DateTime.UtcNow-lastRequest).TotalMilliseconds);
   if(wait>0) Thread.Sleep(wait);
   lastRequest=DateTime.UtcNow;
   try {
    var request=(HttpWebRequest)WebRequest.Create(url);
    request.Method="POST";request.Timeout=Math.Max(5,timeoutSeconds)*1000;request.ReadWriteTimeout=Math.Max(5,timeoutSeconds)*1000;
    request.UserAgent=UserAgent;request.Accept="application/json, text/plain, */*";request.KeepAlive=false;
    request.ContentType="application/x-www-form-urlencoded; charset=UTF-8";
    request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
    request.Referer=referer??"https://www.bilibili.com/";
    request.Headers["Origin"]="https://www.bilibili.com";
    string header=userCookie.Length>0?(cookie.Length>0?userCookie+"; "+cookie:userCookie):cookie;
    if(header.Length>0) request.Headers[HttpRequestHeader.Cookie]=header;
    byte[] payload=Encoding.UTF8.GetBytes(body??"");
    request.ContentLength=payload.Length;
    using(Stream stream=request.GetRequestStream()) stream.Write(payload,0,payload.Length);
    using(HttpWebResponse response=(HttpWebResponse)request.GetResponse()) {
     string text=ReadBody(response);
     if(text.Length>0&&text.TrimStart().StartsWith("<",StringComparison.Ordinal)) throw new BiliException("B站返回了风控页面，评论没有发送。",-412,(int)response.StatusCode);
     return text;
    }
   } catch(BiliException) {throw;}
   catch(WebException e) {
    HttpWebResponse response=e.Response as HttpWebResponse;
    int status=response==null?0:(int)response.StatusCode;
    string text="";try {text=ReadBody(response);} catch {}
    if(status==412||status==429) throw new BiliException("B站风控拦住了评论，等一会儿再试。",-412,status);
    string detail=ExtractError(text);
    if(detail.Length==0) detail=e.Message;
    throw new BiliException(detail,-1,status);
   } catch(Exception e) {
    throw new BiliException("发送评论时出了点问题："+e.Message,-1,0);
   }
  }
  static string ReadBody(HttpWebResponse response) {
   if(response==null) return "";
   try {using(Stream stream=response.GetResponseStream()) {if(stream==null)return "";using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd();}} catch {return "";}
  }
  static string ExtractError(string body) {
   if(string.IsNullOrEmpty(body)) return "";
   try {BiliErrorEnvelope env=Deserialize<BiliErrorEnvelope>(body);if(env!=null&&!string.IsNullOrEmpty(env.message))return ApiMessage(env.code,env.message);} catch {}
   body=StripHtml(body);return body.Length>160?body.Substring(0,160):body;
  }
  static T Deserialize<T>(string json) where T:class {
   if(string.IsNullOrEmpty(json)) return null;
   try {
    using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json))) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
   } catch {return null;}
  }
 }
 [DataContract] sealed class BiliFingerEnvelope {
  [DataMember] public int code;
  [DataMember] public BiliFingerData data;
 }
 [DataContract] sealed class BiliFingerData {
  [DataMember] public string b_3;
  [DataMember] public string b_4;
 }
 [DataContract] sealed class BiliNavEnvelope {
  [DataMember] public int code;
  [DataMember] public BiliNavData data;
 }
 [DataContract] sealed class BiliNavData {
  [DataMember] public BiliWbiImg wbi_img;
  [DataMember] public bool isLogin;
  [DataMember] public string uname;
  [DataMember] public long mid;
 }
 [DataContract] sealed class BiliWbiImg {
  [DataMember] public string img_url;
  [DataMember] public string sub_url;
 }
 [DataContract] sealed class BiliSearchEnvelope {
  [DataMember] public int code;
  [DataMember] public string message;
  [DataMember] public BiliSearchData data;
 }
 [DataContract] sealed class BiliSearchData {
  [DataMember] public BiliSearchItem[] result;
 }
 [DataContract] sealed class BiliSearchItem {
  [DataMember] public string type;
  [DataMember] public string bvid;
  [DataMember] public long aid;
  [DataMember] public string title;
  [DataMember] public string author;
  [DataMember] public string description;
  [DataMember] public string pic;
  [DataMember] public string tag;
  [DataMember] public string duration;
  [DataMember] public long pubdate;
  [DataMember] public long play;
  [DataMember] public long video_review;
  [DataMember] public long favorites;
 }
 [DataContract] sealed class BiliViewEnvelope {
  [DataMember] public int code;
  [DataMember] public string message;
  [DataMember] public BiliViewData data;
 }
 [DataContract] sealed class BiliViewData {
  [DataMember] public string bvid;
  [DataMember] public long aid;
  [DataMember] public long cid;
  [DataMember] public string title;
  [DataMember] public string desc;
  [DataMember] public string pic;
  [DataMember] public int duration;
  [DataMember] public long pubdate;
  [DataMember] public BiliViewOwner owner;
  [DataMember] public BiliViewStat stat;
 }
 [DataContract] sealed class BiliViewOwner {
  [DataMember] public string name;
  [DataMember] public long mid;
 }
 [DataContract] sealed class BiliViewStat {
  [DataMember] public long view;
  [DataMember] public long danmaku;
  [DataMember] public long reply;
  [DataMember] public long favorite;
  [DataMember] public long coin;
  [DataMember] public long share;
  [DataMember] public long like;
 }
 [DataContract] sealed class BiliRankEnvelope {
  [DataMember] public int code;
  [DataMember] public string message;
  [DataMember] public BiliRankData data;
 }
 [DataContract] sealed class BiliRankData {
  [DataMember] public BiliRankItem[] list;
 }
 [DataContract] sealed class BiliRankItem {
  [DataMember] public string bvid;
  [DataMember] public long aid;
  [DataMember] public long cid;
  [DataMember] public string title;
  [DataMember] public string desc;
  [DataMember] public string pic;
  [DataMember] public int duration;
  [DataMember] public long pubdate;
  [DataMember] public BiliViewOwner owner;
  [DataMember] public BiliViewStat stat;
 }
 [DataContract] sealed class BiliReplyEnvelope {
  [DataMember] public int code;
  [DataMember] public string message;
  [DataMember] public BiliReplyData data;
 }
 [DataContract] sealed class BiliReplyData {
  [DataMember] public BiliReply[] replies;
 }
 [DataContract] sealed class BiliReply {
  [DataMember] public long like;
  [DataMember] public long ctime;
  [DataMember] public BiliReplyMember member;
  [DataMember] public BiliReplyContent content;
 }
 [DataContract] sealed class BiliReplyMember {
  [DataMember] public string uname;
 }
 [DataContract] sealed class BiliReplyContent {
  [DataMember] public string message;
 }
 [DataContract] sealed class BiliErrorEnvelope {
  [DataMember] public int code;
  [DataMember] public string message;
 }
 [DataContract] sealed class BiliCommentAddEnvelope {
  [DataMember] public int code;
  [DataMember] public string message;
  [DataMember] public BiliCommentAddData data;
 }
 [DataContract] sealed class BiliCommentAddData {
  [DataMember] public long rpid;
 }
 [DataContract] sealed class BiliDeleteEnvelope {
  [DataMember] public int code;
  [DataMember] public string message;
 }
}
