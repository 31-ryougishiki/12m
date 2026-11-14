using System;
using System.IO;
using System.Collections.Generic;
namespace ElymsPet {
 static class ArchiveCloudFlowTests {
  sealed class Scheduler:IWorkScheduler {public Queue<Action> jobs=new Queue<Action>();public Queue<Action> completions=new Queue<Action>();public void Queue(Action a){jobs.Enqueue(a);}public void Post(Action a){completions.Enqueue(a);}public void Work(){jobs.Dequeue()();}public void Done(){completions.Dequeue()();}}
  sealed class Remote:IArchiveCloudClient {public byte[] data;public int uploads;public bool fail;public string Upload(byte[] b){if(fail)throw new IOException("模拟断网");data=b;uploads++;return "elyms/archives/test.json";}public byte[] Download(string key){return data;}public List<ArchiveCloudVersion> List(){return new List<ArchiveCloudVersion>{new ArchiveCloudVersion{Key="elyms/archives/test.json",Modified="test"}};}}
  static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS cloud flow: "+name);}
  public static void Run(string root){
   string original=Storage.Folder;Storage.Folder=Path.Combine(root,"cloud-flow");
   var scheduler=new Scheduler();var remote=new Remote();var repo=new TransientRepository<ArchiveBook>(ArchiveStore.Normalize(new ArchiveBook()));
   var service=new ArchiveService(new PetState(),scheduler,null,repo,false,null,remote);
   try {
    var config=new ArchiveCloudConfig{Bucket="test-bucket"};config.SetCredentials("test-id","test-secret");Check(service.ConfigureCloud(config),"configure");
    service.Rename("云端原始版本");service.UploadCloud(false);Check(service.CloudBusy,"upload starts asynchronously");scheduler.Work();scheduler.Done();Check(remote.uploads==1&&!service.CloudBusy,"upload completed");
    service.UploadCloud(false);Check(remote.uploads==1&&scheduler.jobs.Count==0,"unchanged archive skips auto upload");
    service.Rename("本地新内容");remote.fail=true;service.UploadCloud(false);scheduler.Work();scheduler.Done();Check(service.Slot.Name=="本地新内容"&&!service.CloudBusy,"failed upload retains local state and releases busy");remote.fail=false;
    var version=new ArchiveCloudVersion{Key="elyms/archives/test.json"};
    service.RestoreCloud(version);scheduler.Work();service.Rename("下载时继续编辑");scheduler.Done();Check(service.Slot.Name=="下载时继续编辑","concurrent edit blocks restore");
    service.RestoreCloud(version);scheduler.Work();scheduler.Done();Check(service.Slot.Name=="云端原始版本","restore replaces in-memory and repository data");Check(repo.Load().Slots[0].Name=="云端原始版本","restore persisted");
    var backups=Directory.GetFiles(Path.Combine(ArchiveStore.Folder,"cloud-backups"),"*.json");Check(backups.Length==1&&ArchiveCloudCodec.Decode(File.ReadAllBytes(backups[0])).Slots[0].Name=="下载时继续编辑","restore preserves exact pre-restore backup");
    service.Rename("关闭前编辑");service.UploadCloud(true);service.Dispose();scheduler.Work();Check(remote.uploads==1,"disposed service cancels queued cloud upload");
   }finally{service.Dispose();Storage.Folder=original;}
  }
 }
}
