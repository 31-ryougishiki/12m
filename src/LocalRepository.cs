using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace ElymsPet {
 [DataContract] public sealed class LocalRepositoryMapping {
  [DataMember] public string Repository="",Folder="";
 }
 [DataContract] public sealed class LocalRepositorySettings {
  [DataMember] public string Editor="";
  [DataMember] public List<LocalRepositoryMapping> Repositories=new List<LocalRepositoryMapping>();
 }
 public sealed class LocalCodeTarget {public string File,Message;public int Line;public bool Exact;}
 public static class LocalRepository {
  public static string SettingsPath {get{return Path.Combine(Storage.Folder,"local-repositories.json");}}
  public static LocalRepositorySettings Load(){string error;var settings=JsonFile.Load<LocalRepositorySettings>(SettingsPath,out error)??new LocalRepositorySettings();if(settings.Repositories==null)settings.Repositories=new List<LocalRepositoryMapping>();return settings;}
  public static bool Save(LocalRepositorySettings settings,out string error){return JsonFile.Save(SettingsPath,settings,Storage.Folder,out error);}
  public static string Repository(string url){var match=Regex.Match(url??"",@"^https://github\.com/([\w.-]+/[\w.-]+)/blob/");return match.Success?match.Groups[1].Value:"";}
  public static string MappedFolder(LocalRepositorySettings settings,string url){var mapping=settings.Repositories.Find(delegate(LocalRepositoryMapping m){return m!=null&&string.Equals(m.Repository,Repository(url),StringComparison.OrdinalIgnoreCase);});return mapping==null?"":mapping.Folder;}
  public static string ResolveFile(string folder,string url){
   if(!RepositoryLearning.PinnedCode(url))throw new InvalidDataException("需要固定提交的源码行链接。");
   if(string.IsNullOrWhiteSpace(folder)||!Path.IsPathRooted(folder)||!Directory.Exists(folder))throw new DirectoryNotFoundException("请先选择存在的本地仓库根目录。");
   var match=Regex.Match(url,@"/blob/[a-fA-F0-9]{40}/([^?#]+)#");string relative=Uri.UnescapeDataString(match.Groups[1].Value);
   foreach(string part in relative.Split('/'))if(part.Length==0||part=="."||part==".."||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0||part.EndsWith(".")||part.EndsWith(" "))throw new InvalidDataException("源码路径无效。");
   string root=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
   string file=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
   if(!file.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("源码位置超出仓库目录。");
   if(!System.IO.File.Exists(file))throw new FileNotFoundException("本地仓库中没有这个文件，请检查仓库映射："+relative);
   for(string current=file;current!=null&&current.Length>=root.TrimEnd(Path.DirectorySeparatorChar).Length;current=Path.GetDirectoryName(current))if((System.IO.File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("源码路径包含链接目录，请映射到真实仓库目录。");
   return file;
  }
  static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
  static string GitBlob(string folder,string spec){
   try{using(var process=Process.Start(new ProcessStartInfo("git.exe","-C "+Quote(folder)+" rev-parse --verify "+Quote(spec)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){if(!process.WaitForExit(2000)){process.Kill();return "";}return process.ExitCode==0?process.StandardOutput.ReadToEnd().Trim():"";}}catch{return "";}
  }
  public static LocalCodeTarget Resolve(string folder,string url){
   string file=ResolveFile(folder,url);var match=Regex.Match(url,@"/blob/([a-fA-F0-9]{40})/([^?#]+)#L([1-9][0-9]*)");int line;if(!int.TryParse(match.Groups[3].Value,out line))throw new InvalidDataException("代码行号无效。");
   var target=new LocalCodeTarget{File=file,Line=1,Message="本地文件与来源版本不同或无法核实，已打开文件开头；准确行号请查看网页源码。"};
   string expected=GitBlob(folder,match.Groups[1].Value+":"+Uri.UnescapeDataString(match.Groups[2].Value));
   if(Regex.IsMatch(expected,"^[a-fA-F0-9]{40}$")&&new FileInfo(file).Length<=8*1024*1024){byte[] bytes=System.IO.File.ReadAllBytes(file);using(var sha=SHA1.Create()){byte[] header=Encoding.ASCII.GetBytes("blob "+bytes.Length+"\0");sha.TransformBlock(header,0,header.Length,header,0);sha.TransformFinalBlock(bytes,0,bytes.Length);string hash=BitConverter.ToString(sha.Hash).Replace("-","");if(string.Equals(hash,expected,StringComparison.OrdinalIgnoreCase)){target.Exact=true;target.Line=line;target.Message="已定位本地源码第 "+line+" 行（文件与来源提交一致）。";}}}
   return target;
  }
  public static string ReadContext(LocalRepositorySettings settings,string url){
   string prefix="来源："+url+"\n";
   try{
    if(!RepositoryLearning.PinnedCode(url))return prefix+"未读取：不是固定提交的源码行链接。";
    string folder=MappedFolder(settings,url);if(string.IsNullOrEmpty(folder))return prefix+"未读取：尚未配置该仓库的本地映射。";
    string file=ResolveFile(folder,url);var match=Regex.Match(url,@"/blob/([a-fA-F0-9]{40})/([^?#]+)#L([1-9][0-9]*)(?:-L([1-9][0-9]*))?");int line;
    if(!int.TryParse(match.Groups[3].Value,out line))return prefix+"未读取：行号无效。";
    byte[] bytes;
    // Bound the stream itself, not only a preceding stat. Hash and decode the same bytes.
    using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){
     if(stream.Length>8*1024*1024)return prefix+"未读取：文件超过 8 MB。";
     bytes=new byte[(int)stream.Length];int offset=0,read;while(offset<bytes.Length&&(read=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=read;
     if(offset!=bytes.Length)return prefix+"未读取：文件读取不完整。";
    }
    string expected=GitBlob(folder,match.Groups[1].Value+":"+Uri.UnescapeDataString(match.Groups[2].Value));
    using(var sha=SHA1.Create()){byte[] header=Encoding.ASCII.GetBytes("blob "+bytes.Length+"\0");sha.TransformBlock(header,0,header.Length,header,0);sha.TransformFinalBlock(bytes,0,bytes.Length);string hash=BitConverter.ToString(sha.Hash).Replace("-","");if(!string.Equals(hash,expected,StringComparison.OrdinalIgnoreCase))return prefix+"未读取正文：本地文件与来源提交不一致或无法核实。不能使用旧行号解释当前文件，请核对仓库版本。";}
    string decoded=new UTF8Encoding(false,true).GetString(bytes);if(decoded.IndexOf('\0')>=0)return prefix+"未读取正文：文件不是可用文本。";
    var lines=decoded.Split('\n');if(line>lines.Length)return prefix+"未读取正文：来源行号超出文件范围。";
    int endLine;if(!int.TryParse(match.Groups[4].Value,out endLine))endLine=line;
    int start=Math.Max(1,line-12),end=(int)Math.Min(lines.Length,Math.Min((long)start+95,Math.Max((long)line+40,(long)endLine+8)));
    int preceding=0;for(int i=start;i<line;i++)preceding+=Math.Min(1000,lines[i-1].Length)+32;
    while(start<line&&preceding>2000){preceding-=Math.Min(1000,lines[start-1].Length)+32;start++;}
    var body=new StringBuilder();int actual=start-1;bool clipped=false;
    for(int i=start;i<=end;i++){string value=lines[i-1].TrimEnd('\r');if(value.Length>1000){value=value.Substring(0,1000)+" …[本行截断]";clipped=true;}if(body.Length+value.Length>6500){clipped=true;break;}body.Append("L").Append(i).Append(": ").Append(value).Append("\n");actual=i;}
    return prefix+"已读取并核对文件内容与固定提交一致；片段 L"+start+"–L"+actual+"，目标 L"+line+"。仅此片段，未读取完整仓库/PR。"+(clipped||endLine>actual?"内容已截断。":"")+"\n"+body;
   }catch{return prefix+"未读取：本地文件不可用或不是可解码的 UTF-8 源码；请检查仓库映射和版本。";}
  }
  public static string FindEditor(){
   foreach(string path in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){try{string candidate=Path.GetFullPath(Path.Combine(path.Trim('"'),"..","Code.exe"));if(System.IO.File.Exists(candidate))return candidate;}catch{}}
   string usual=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Microsoft VS Code","Code.exe");return System.IO.File.Exists(usual)?usual:"";
  }
  public static ProcessStartInfo OpenInfo(string editor,LocalCodeTarget target){
   string name=Path.GetFileName(editor??"");if(!Path.IsPathRooted(editor??"")||!System.IO.File.Exists(editor)||(!string.Equals(name,"Code.exe",StringComparison.OrdinalIgnoreCase)&&!string.Equals(name,"Cursor.exe",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("请选择 VS Code 的 Code.exe 或 Cursor.exe。");
   if(target==null||!System.IO.File.Exists(target.File)||target.Line<1)throw new InvalidDataException("本地代码目标无效。");
   return new ProcessStartInfo(editor,"--reuse-window --goto "+Quote(target.File+":"+target.Line+":1")){UseShellExecute=false};
  }
 }
 public static class LearningReadability {
  public static List<string> Parts(string value){
   var parts=new List<string>();var current=new StringBuilder();bool code=false;
   foreach(char c in value??""){if(c=='`')code=!code;current.Append(c);if(!code&&(c=='\n'||c=='。'||c=='；')){string part=current.ToString().Trim();if(part.Length>0)parts.Add(part);current.Clear();}}
   if(current.ToString().Trim().Length>0)parts.Add(current.ToString().Trim());return parts;
  }
 }
}
