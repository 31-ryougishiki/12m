using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;

namespace ElymsPet {
 public sealed partial class ArchiveService {
  WorkScope cloudWork;
  IArchiveCloudClient cloudApi;
  IArchiveCloudClient Client(ArchiveCloudConfig config){return cloudApi??new ArchiveOssClient(config);}
  DispatcherTimer cloudTimer;
  ArchiveCloudConfig cloudConfig;
  bool cloudBusy;
  string cloudStatus="尚未配置云同步",uploadedHash="";
  public string CloudStatus {get{return cloudStatus;}}
  public bool CloudBusy {get{return cloudBusy;}}
  public ArchiveCloudConfig CloudConfig {get{return cloudConfig??new ArchiveCloudConfig();}}
  void InitializeCloud(IWorkScheduler scheduler,IArchiveCloudClient injected){
   cloudApi=injected;
   cloudConfig=preview?new ArchiveCloudConfig():ArchiveCloudConfig.Load();
   cloudWork=new WorkScope(scheduler,delegate(string error){CloudDone(error);},!preview);
   if(preview||!PluginAccess.Enabled(state,"cloud"))return;
   cloudStatus=string.IsNullOrEmpty(cloudConfig.ProtectedCredentials)?"尚未配置云同步":"已配置；等待同步";
   cloudTimer=new DispatcherTimer{Interval=TimeSpan.FromMinutes(2)};
   cloudTimer.Tick+=delegate{if(cloudConfig.Automatic&&!busy&&!cloudBusy)UploadCloud(false);};cloudTimer.Start();
  }
  public bool ConfigureCloud(ArchiveCloudConfig config){
   if(preview||cloudBusy||!PluginAccess.Enabled(state,"cloud"))return false;
   try{config.Save();cloudConfig=config;uploadedHash="";CloudDone("设置已保存；可上传或查看云端版本。");return true;}catch(Exception e){CloudDone(e.Message);return false;}
  }
  void CloudDone(string message){cloudBusy=false;cloudStatus=message;Raise();}
  bool CloudStart(string message){
   if(!PluginAccess.Enabled(state,"cloud")){CloudDone("请先安装并启用云同步插件。");return false;}
   if(preview||cloudBusy||work.IsDisposed)return false;
   try{cloudConfig.Validate();}catch(Exception e){CloudDone(e.Message);return false;}
   cloudBusy=true;cloudStatus=message;Raise();return true;
  }
  public void UploadCloud(bool force){
   if(cloudBusy)return;
   if(busy){CloudDone("访谈处理中，请稍后同步。");return;}
   if(!CloudStart("正在上传存档…"))return;
   byte[] snapshot;try{snapshot=ArchiveCloudCodec.Encode(book);}catch(Exception e){CloudDone(e.Message);return;}
   string hash=ArchiveCloudCodec.Hash(snapshot);
   if(!force&&hash==uploadedHash){CloudDone("本地内容没有变化；无需上传。");return;}
   var config=cloudConfig;
   cloudWork.Run<string>(delegate{try{return Client(config).Upload(snapshot);}catch(IOException e){return "ERROR:"+e.Message;}},delegate(string key){if(key.StartsWith("ERROR:")){CloudDone(key.Substring(6));return;}uploadedHash=hash;CloudDone("已上传 · "+DateTime.Now.ToString("HH:mm:ss")+" · 云端保留独立版本");});
  }
  public void ListCloud(Action<List<ArchiveCloudVersion>> done){
   if(!CloudStart("正在读取云端版本…"))return;var config=cloudConfig;
   cloudWork.Run<List<ArchiveCloudVersion>>(delegate{return Client(config).List();},delegate(List<ArchiveCloudVersion> versions){CloudDone("找到 "+versions.Count+" 个云端版本。");done(versions);});
  }
  public void RestoreCloud(ArchiveCloudVersion version){
   if(cloudBusy||version==null)return;
   if(busy){CloudDone("访谈处理中，请稍后恢复。");return;}
   if(!CloudStart("正在下载云端版本…"))return;
   var config=cloudConfig;byte[] original;
   try{original=ArchiveCloudCodec.Encode(book);}catch(Exception e){CloudDone(e.Message);return;}
   string hash=ArchiveCloudCodec.Hash(original);
   cloudWork.Run<ArchiveBook>(delegate{return ArchiveCloudCodec.Decode(Client(config).Download(version.Key));},delegate(ArchiveBook downloaded){
    if(busy||ArchiveCloudCodec.Hash(ArchiveCloudCodec.Encode(book))!=hash){CloudDone("下载期间本地存档发生变化，已取消恢复；请重试。");return;}
    string folder=Path.Combine(ArchiveStore.Folder,"cloud-backups");Directory.CreateDirectory(folder);
    string backup=Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".json");File.WriteAllBytes(backup,original);
    if(!repository.Save(downloaded)){CloudDone("本地写入失败，原存档已保留。");return;}
    book=downloaded;slot=ArchiveStore.Active(book);uploadedHash=ArchiveCloudCodec.Hash(ArchiveCloudCodec.Encode(book));
    string result="已恢复云端版本；恢复前的本地存档已备份至 archives/cloud-backups。";
    try{foreach(var item in book.Slots)ArchiveKnowledge.WriteFiles(item);}catch{result+=" 部分派生文档未生成，请重新保存。";}
    TouchAll();CloudDone(result);
   });
  }
 }
}


