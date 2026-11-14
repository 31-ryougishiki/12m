using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ElymsPet {
 [DataContract] public sealed class ArchiveCloudConfig {
  [DataMember] public string Bucket="", Region="cn-shenzhen", Prefix="elyms/archives/";
  [DataMember] public string ProtectedCredentials="";
  [DataMember] public bool Automatic;
  public static string PathName {get{return Path.Combine(Storage.Folder,"archive-cloud.json");}}
  public static ArchiveCloudConfig Load(){string error;return JsonFile.Load<ArchiveCloudConfig>(PathName,out error)??new ArchiveCloudConfig();}
  public void SetCredentials(string id,string secret){ProtectedCredentials=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(id.Trim()+"\n"+secret.Trim()),null,DataProtectionScope.CurrentUser));}
  public string[] Credentials(){return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedCredentials),null,DataProtectionScope.CurrentUser)).Split('\n');}
  public void Validate(){
   if(!Regex.IsMatch(Bucket??"","^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")||!Regex.IsMatch(Region??"","^[a-z0-9]+(-[a-z0-9]+)+$"))throw new InvalidOperationException("请填写有效的 Bucket 和地域 ID。");
   if(!Regex.IsMatch(Prefix??"","^[a-zA-Z0-9_-]+(/[a-zA-Z0-9_-]+)*/$"))throw new InvalidOperationException("目录请使用英文、数字、短横线或下划线，并以 / 结尾。");
   string[] keys;try{keys=Credentials();}catch{throw new InvalidOperationException("请在本机填写 AccessKey ID 和 Secret。");}
   if(keys.Length!=2||string.IsNullOrWhiteSpace(keys[0])||string.IsNullOrWhiteSpace(keys[1]))throw new InvalidOperationException("访问凭证不完整。");
  }
  public void Save(){Validate();string error;if(!JsonFile.Save(PathName,this,Storage.Folder,out error))throw new IOException("云同步设置保存失败。");}
 }
 public sealed class ArchiveCloudVersion {
  public string Key,Modified;
  public override string ToString(){return Modified+"  ·  "+System.IO.Path.GetFileNameWithoutExtension(Key);}
 }
 public static class ArchiveCloudCodec {
  public const int Limit=32*1024*1024;
  public static byte[] Encode(ArchiveBook book){using(var s=new MemoryStream()){new DataContractJsonSerializer(typeof(ArchiveBook)).WriteObject(s,book);if(s.Length>Limit)throw new InvalidOperationException("存档超过 32 MB，暂不支持云同步。");return s.ToArray();}}
  public static ArchiveBook Decode(byte[] data){
   if(data.Length>Limit)throw new InvalidDataException("云端存档过大。");
   ArchiveBook b;using(var s=new MemoryStream(data))b=(ArchiveBook)new DataContractJsonSerializer(typeof(ArchiveBook)).ReadObject(s);
   if(b==null||b.Version<1||b.Version>4||b.Slots==null||b.Slots.Count==0||b.Slots.Count>ArchiveStore.MaxSlots)throw new InvalidDataException("云端存档版本或结构不兼容。");
   foreach(var slot in b.Slots)if(slot==null||!Regex.IsMatch(slot.Id??"","^[a-fA-F0-9]{32}$"))throw new InvalidDataException("云端存档标识无效。");
   return ArchiveStore.Normalize(b);
  }
  public static string Hash(byte[] data){using(var sha=SHA256.Create())return Hex(sha.ComputeHash(data));}
  public static string Hex(byte[] data){return BitConverter.ToString(data).Replace("-","").ToLowerInvariant();}
 }
 // Limited to HTTPS OSS endpoints; no redirects, credentials never enter URLs or error messages.
 public interface IArchiveCloudClient {string Upload(byte[] data);byte[] Download(string key);List<ArchiveCloudVersion> List();}
 public sealed class ArchiveOssClient : IArchiveCloudClient {
  readonly ArchiveCloudConfig config;
  public ArchiveOssClient(ArchiveCloudConfig config){config.Validate();this.config=config;}
  static byte[] Hmac(byte[] key,string value){using(var h=new HMACSHA256(key))return h.ComputeHash(Encoding.UTF8.GetBytes(value));}
  public static string Sign(string secret,string region,string stamp,string canonical){
   string date=stamp.Substring(0,8),scope=date+"/"+region+"/oss/aliyun_v4_request";
   byte[] key=Hmac(Encoding.UTF8.GetBytes("aliyun_v4"+secret),date);key=Hmac(key,region);key=Hmac(key,"oss");key=Hmac(key,"aliyun_v4_request");
   return ArchiveCloudCodec.Hex(Hmac(key,"OSS4-HMAC-SHA256\n"+stamp+"\n"+scope+"\n"+ArchiveCloudCodec.Hash(Encoding.UTF8.GetBytes(canonical))));
  }
  static string EscapePath(string s){return Uri.EscapeDataString(s).Replace("%2F","/");}
  byte[] Request(string method,string key,string query,byte[] body){
   string stamp=DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ",System.Globalization.CultureInfo.InvariantCulture);
   string[] credentials=config.Credentials();
   var headers=new SortedDictionary<string,string>(StringComparer.Ordinal);
   headers.Add("x-oss-content-sha256","UNSIGNED-PAYLOAD");headers.Add("x-oss-date",stamp);
   if(body!=null){headers.Add("content-type","application/json");using(var md5=MD5.Create())headers.Add("content-md5",Convert.ToBase64String(md5.ComputeHash(body)));headers.Add("x-oss-forbid-overwrite","true");}
   var canonicalHeaders=new StringBuilder();foreach(var pair in headers)canonicalHeaders.Append(pair.Key).Append(':').Append(pair.Value).Append('\n');
   string canonical=method+"\n"+EscapePath("/"+config.Bucket+"/"+key)+"\n"+query+"\n"+canonicalHeaders+"\n\nUNSIGNED-PAYLOAD";
   var request=(HttpWebRequest)WebRequest.Create("https://"+config.Bucket+".oss-"+config.Region+".aliyuncs.com/"+EscapePath(key)+(query.Length==0?"":"?"+query));
   request.Method=method;request.AllowAutoRedirect=false;request.Timeout=30000;request.ReadWriteTimeout=30000;
   foreach(var pair in headers)if(pair.Key=="content-type")request.ContentType=pair.Value;else request.Headers[pair.Key]=pair.Value;
   request.Headers["Authorization"]="OSS4-HMAC-SHA256 Credential="+credentials[0]+"/"+stamp.Substring(0,8)+"/"+config.Region+"/oss/aliyun_v4_request, Signature="+Sign(credentials[1],config.Region,stamp,canonical);
   try {
    if(body!=null){request.ContentLength=body.Length;using(var stream=request.GetRequestStream())stream.Write(body,0,body.Length);}
    using(var response=(HttpWebResponse)request.GetResponse()){
     if((int)response.StatusCode<200||(int)response.StatusCode>=300)throw new IOException("OSS 返回非成功状态。");
     using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){
      var buffer=new byte[8192];int n;while((n=stream.Read(buffer,0,buffer.Length))>0){if(output.Length+n>ArchiveCloudCodec.Limit)throw new InvalidDataException("云端响应超过大小限制。");output.Write(buffer,0,n);}return output.ToArray();
     }
    }
   }catch(WebException e){var response=e.Response as HttpWebResponse;int code=response==null?0:(int)response.StatusCode;if(response!=null)response.Dispose();throw new IOException(code==403?"OSS 拒绝访问，请检查 RAM 权限、密钥和地域。":code==0?"无法连接 OSS，请检查网络后重试。":"OSS 请求失败（HTTP "+code+"）。");}
  }
  public string Upload(byte[] data){ArchiveCloudCodec.Decode(data);string key=config.Prefix+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N")+".json";Request("PUT",key,"",data);return key;}
  public byte[] Download(string key){if(!key.StartsWith(config.Prefix,StringComparison.Ordinal)||!Regex.IsMatch(key.Substring(config.Prefix.Length),"^[a-zA-Z0-9-]+[.]json$"))throw new InvalidDataException("无效的云端版本路径。");return Request("GET",key,"",null);}
  public List<ArchiveCloudVersion> List(){
   var result=new List<ArchiveCloudVersion>();string marker="";
   do {
    string query=(marker.Length>0?"marker="+Uri.EscapeDataString(marker)+"&":"")+"max-keys=1000&prefix="+Uri.EscapeDataString(config.Prefix);
    byte[] bytes=Request("GET","",query,null);var doc=new XmlDocument{XmlResolver=null};
    using(var reader=XmlReader.Create(new MemoryStream(bytes),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))doc.Load(reader);
    foreach(XmlNode n in doc.SelectNodes("/ListBucketResult/Contents")){string key=n["Key"].InnerText;if(key.EndsWith(".json",StringComparison.Ordinal))result.Add(new ArchiveCloudVersion{Key=key,Modified=n["LastModified"].InnerText});}
    string next="";if(doc.SelectSingleNode("/ListBucketResult/IsTruncated").InnerText=="true"){var nextNode=doc.SelectSingleNode("/ListBucketResult/NextMarker");var lastKey=doc.SelectSingleNode("/ListBucketResult/Contents[last()]/Key");next=nextNode!=null?nextNode.InnerText:lastKey==null?"":lastKey.InnerText;if(next.Length==0)throw new IOException("OSS 分页缺少游标。");}
    if(next.Length>0&&next==marker)throw new IOException("OSS 分页未前进。");marker=next;
    if(result.Count>50000)throw new IOException("云端版本过多，请先在 OSS 归档旧版本。");
   }while(marker.Length>0);
   result.Sort(delegate(ArchiveCloudVersion a,ArchiveCloudVersion b){return string.CompareOrdinal(b.Key,a.Key);});return result;
  }
 }
}

