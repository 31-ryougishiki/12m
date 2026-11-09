using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using QRCoder;

#pragma warning disable 0649
namespace ElymsPet {
 public sealed class BiliQrSession {
  public bool Ok;
  public string Error="", Key="", Url="";
  public BitmapSource Image;
  public DateTime ExpiresAt=DateTime.MinValue;
 }
 public sealed class BiliLoginPoll {
  public bool Ok;
  public string Status="error", Message="", Error="";
  public BiliAccountInfo Account;
 }
 /// <summary>B 站扫码登录：只在用户手机确认后保存 Cookie，使用 Windows DPAPI 加密。</summary>
 public static class BiliAuth {
  const string Passport="https://passport.bilibili.com";
  const string UserAgent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
  static readonly object Gate=new object();
  static readonly string FilePath=Path.Combine(Storage.Folder,"bili-account.dat");
  static BiliAccountInfo current=new BiliAccountInfo();
  public static BiliAccountInfo Current {get {return current;}}
  public static bool HasLogin {get {return BiliApi.HasLogin;}}
  public static void Load() {
   lock(Gate) {
    try {
     if(!File.Exists(FilePath)) return;
     byte[] encrypted=File.ReadAllBytes(FilePath);
     byte[] raw=ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser);
     string cookie=Encoding.UTF8.GetString(raw).Trim();
     if(cookie.Length==0) return;
     BiliApi.SetUserCookie(cookie);
     current=new BiliAccountInfo {LoggedIn=true,Name="已保存登录",Mid=BiliApi.CookieValue(cookie,"DedeUserID"),Error=""};
    } catch {BiliApi.ClearUserCookie();current=new BiliAccountInfo();}
   }
  }
  public static BiliQrSession StartQr(int timeoutSeconds) {
   try {
    string body=Get(Passport+"/x/passport-login/web/qrcode/generate?source=main-fe-header",timeoutSeconds);
    BiliQrEnvelope env=Deserialize<BiliQrEnvelope>(body);
    if(env==null||env.code!=0||env.data==null||string.IsNullOrEmpty(env.data.url)) return new BiliQrSession {Error=env==null?"B站没有返回登录二维码。":(env.message??"没有拿到登录二维码。")};
    var session=new BiliQrSession {Ok=true,Key=env.data.qrcode_key??"",Url=env.data.url,ExpiresAt=DateTime.UtcNow.AddSeconds(180)};
    session.Image=RenderQr(env.data.url);
    return session;
   } catch(Exception e) {
    return new BiliQrSession {Error="生成登录二维码失败："+e.Message};
   }
  }
  public static BiliLoginPoll Poll(string key,int timeoutSeconds) {
   if(string.IsNullOrEmpty(key)) return new BiliLoginPoll {Error="登录会话为空。"};
   try {
    string setCookie;
    string body=GetWithCookies(Passport+"/x/passport-login/web/qrcode/poll?qrcode_key="+Uri.EscapeDataString(key),timeoutSeconds,out setCookie);
    BiliQrPollEnvelope env=Deserialize<BiliQrPollEnvelope>(body);
    if(env==null||env.data==null) return new BiliLoginPoll {Error="B站没有返回扫码状态。"};
    int code=env.data.code;
    if(code==86101) return new BiliLoginPoll {Ok=true,Status="pending",Message="等待扫码"};
    if(code==86090) return new BiliLoginPoll {Ok=true,Status="scanned",Message="已扫码，请在手机上确认"};
    if(code==86038) return new BiliLoginPoll {Ok=true,Status="expired",Message="二维码已过期"};
    if(code!=0) return new BiliLoginPoll {Ok=true,Status="pending",Message=string.IsNullOrEmpty(env.data.message)?"等待确认":env.data.message};
    string cookie=LoginCookie(env.data.url);
    if(cookie.Length==0) cookie=setCookie;
    if(cookie.Length==0) return new BiliLoginPoll {Error="登录成功，但 B站没有返回会话 Cookie。"};
    string full=cookie;
    string anon=BiliApi.AnonymousCookie();
    if(anon.Length>0) full+="; "+anon;
    BiliApi.SetUserCookie(full);
    BiliAccountInfo account=BiliApi.GetAccount(20);
    if(account==null||!account.LoggedIn) {
     BiliApi.ClearUserCookie();
     return new BiliLoginPoll {Error=account==null?"登录校验失败。":account.Error};
    }
    SaveCookie(full);
    current=account;
    return new BiliLoginPoll {Ok=true,Status="confirmed",Message="登录成功",Account=account};
   } catch(Exception e) {
    return new BiliLoginPoll {Error="检查扫码状态失败："+e.Message};
   }
  }
  public static void Logout() {
   lock(Gate) {
    BiliApi.ClearUserCookie();
    current=new BiliAccountInfo();
    try {if(File.Exists(FilePath))File.Delete(FilePath);} catch {}
   }
  }
  static void SaveCookie(string cookie) {
   try {
    Directory.CreateDirectory(Storage.Folder);
    byte[] raw=Encoding.UTF8.GetBytes(cookie??"");
    byte[] encrypted=ProtectedData.Protect(raw,null,DataProtectionScope.CurrentUser);
    File.WriteAllBytes(FilePath,encrypted);
   } catch {}
  }
  static string LoginCookie(string url) {
   if(string.IsNullOrEmpty(url)) return "";
   Uri uri;
   if(!Uri.TryCreate(url,UriKind.Absolute,out uri)) return "";
   string[] names={"SESSDATA","bili_jct","DedeUserID","DedeUserID__ckMd5","sid","buvid3","buvid4"};
   var text=new StringBuilder();
   for(int i=0;i<names.Length;i++) {
    string value=QueryValue(uri,names[i]);
    if(value.Length==0) continue;
    if(text.Length>0) text.Append("; ");
    text.Append(names[i]).Append('=').Append(value);
   }
   return text.ToString();
  }
  static string QueryValue(Uri uri,string name) {
   string query=(uri.Query??"").TrimStart('?');
   string[] parts=query.Split('&');
   for(int i=0;i<parts.Length;i++) {
    int eq=parts[i].IndexOf('=');
    if(eq<=0) continue;
    if(Uri.UnescapeDataString(parts[i].Substring(0,eq))==name) return Uri.UnescapeDataString(parts[i].Substring(eq+1));
   }
   return "";
  }
  static string Get(string url,int timeoutSeconds) {string ignored;return GetWithCookies(url,timeoutSeconds,out ignored);}
  static string GetWithCookies(string url,int timeoutSeconds,out string setCookie) {
   setCookie="";
   var jar=new CookieContainer();
   var request=(HttpWebRequest)WebRequest.Create(url);
   request.Method="GET";request.Timeout=Math.Max(5,timeoutSeconds)*1000;request.ReadWriteTimeout=Math.Max(5,timeoutSeconds)*1000;
   request.UserAgent=UserAgent;request.Accept="application/json, text/plain, */*";request.KeepAlive=false;
   request.Referer="https://www.bilibili.com/";
   request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
   request.CookieContainer=jar;
   using(HttpWebResponse response=(HttpWebResponse)request.GetResponse()) {
    string body="";
    using(Stream stream=response.GetResponseStream()) {
     if(stream!=null) using(var reader=new StreamReader(stream,Encoding.UTF8)) body=reader.ReadToEnd();
    }
    setCookie=SessionCookie(jar);
    if(setCookie.Length==0) setCookie=HeaderCookie(response.Headers["Set-Cookie"]);
    return body;
   }
  }
  static string SessionCookie(CookieContainer jar) {
   string[] names={"SESSDATA","bili_jct","DedeUserID","DedeUserID__ckMd5","sid"};
   var found=new string[names.Length];
   for(int i=0;i<names.Length;i++) {
    try {
     CookieCollection cookies=jar.GetCookies(new Uri("https://passport.bilibili.com"));
     for(int j=0;j<cookies.Count;j++) if(cookies[j].Name==names[i]) found[i]=cookies[j].Value;
     if(string.IsNullOrEmpty(found[i])) {
      cookies=jar.GetCookies(new Uri("https://www.bilibili.com"));
      for(int j=0;j<cookies.Count;j++) if(cookies[j].Name==names[i]) found[i]=cookies[j].Value;
     }
    } catch {}
   }
   var text=new StringBuilder();
   for(int i=0;i<names.Length;i++) {
    if(string.IsNullOrEmpty(found[i])) continue;
    if(text.Length>0) text.Append("; ");
    text.Append(names[i]).Append('=').Append(found[i]);
   }
   return text.ToString();
  }
  static string HeaderCookie(string header) {
   if(string.IsNullOrEmpty(header)) return "";
   string[] names={"SESSDATA","bili_jct","DedeUserID","DedeUserID__ckMd5","sid"};
   var values=new string[names.Length];
   string[] parts=header.Split(',');
   for(int p=0;p<parts.Length;p++) {
    string first=parts[p].Split(';')[0].Trim();
    int eq=first.IndexOf('=');
    if(eq<=0) continue;
    string name=first.Substring(0,eq).Trim();
    string value=first.Substring(eq+1).Trim();
    for(int i=0;i<names.Length;i++) if(name==names[i]) values[i]=value;
   }
   var text=new StringBuilder();
   for(int i=0;i<names.Length;i++) {
    if(string.IsNullOrEmpty(values[i])) continue;
    if(text.Length>0) text.Append("; ");
    text.Append(names[i]).Append('=').Append(values[i]);
   }
   return text.ToString();
  }
  static BitmapSource RenderQr(string content) {
   using(var generator=new QRCodeGenerator())
   using(var data=generator.CreateQrCode(content,QRCodeGenerator.ECCLevel.Q))
   using(var qr=new QRCode(data))
   using(Bitmap bitmap=qr.GetGraphic(8)) {
    using(var stream=new MemoryStream()) {
     bitmap.Save(stream,ImageFormat.Png);
     stream.Position=0;
     var image=new BitmapImage();
     image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
     return image;
    }
   }
  }
  static T Deserialize<T>(string json) where T:class {
   if(string.IsNullOrEmpty(json)) return null;
   try {using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json))) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);} catch {return null;}
  }
 }
 [DataContract] sealed class BiliQrEnvelope {
  [DataMember] public int code;
  [DataMember] public string message;
  [DataMember] public BiliQrData data;
 }
 [DataContract] sealed class BiliQrData {
  [DataMember] public string url;
  [DataMember] public string qrcode_key;
 }
 [DataContract] sealed class BiliQrPollEnvelope {
  [DataMember] public int code;
  [DataMember] public string message;
  [DataMember] public BiliQrPollData data;
 }
 [DataContract] sealed class BiliQrPollData {
  [DataMember] public int code;
  [DataMember] public string message;
  [DataMember] public string url;
 }
}
