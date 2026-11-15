using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using ElymsPet;

// Validate using the real graph editor against a staged repository, then commit
// once through ArchiveService.Save. Never partially import a 44-module batch.
public static class ImportGlobalModules {
 [DataContract] public sealed class Receipt {
  [DataMember] public string Status,TopicId,SlotId,Revision;
  [DataMember] public int Modules,Nodes;
 }
 sealed class Scheduler:IWorkScheduler {public void Queue(Action a){throw new InvalidOperationException("No background/model work during import");}public void Post(Action a){a();}}
 sealed class Staged:IRepository<ArchiveBook> {
  public ArchiveBook Book;public bool Commit;public string OriginalHash;public string LastError{get;private set;}
  public ArchiveBook Load(){return Book;}
  public bool Save(ArchiveBook value){
   Book=value;if(!Commit)return true;
   if(Hash(ArchiveStore.FilePath)!=OriginalHash){LastError="Archive changed during validation; retry with fresh state";return false;}
   bool ok=ArchiveStore.Save(value);if(!ok)LastError="ArchiveStore.Save failed";return ok;
  }
 }
 static string Hash(string path){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path)));}
 [STAThread] public static int Main(string[] args){
  try{
   var graph=GraphEditing.Parse<DomainGraph>(File.ReadAllText(args[0]));string folder=Path.GetDirectoryName(Path.GetFullPath(args[0]));
   if(graph==null||graph.Nodes.Count==0||graph.Architecture==null)throw new Exception("Missing generated graph");
   string invalid=RepositoryArchitecture.Validate(graph,graph.Architecture);if(invalid.Length>0)throw new Exception(invalid);
   if(Process.GetProcessesByName("ElymsPet").Length>0)throw new Exception("Live pet detected; use graph-edit protocol instead of offline import");
   string hash=Hash(ArchiveStore.FilePath);var book=ArchiveStore.Load();var repo=new Staged{Book=book,OriginalHash=hash};
   var preserved=new Dictionary<string,string>();foreach(var slot in book.Slots)foreach(var g in slot.DomainGraphs)preserved.Add(slot.Id+"/"+g.Id,GraphEditing.Json(g));
   var state=Storage.Load();
   using(var service=new ArchiveService(state,new Scheduler(),new PetFeedback((a,b,c)=>{},(a,b)=>{},()=>{}),repo,true)){
    if(service.Slot.DomainGraphs.Exists(g=>g.Id==graph.Id))throw new Exception("Destination graph exists; refusing overwrite");
    var map=GraphEditing.Parse<RepositoryMap>(GraphEditing.Json(graph.Architecture));foreach(var m in map.Modules)m.Nodes.Clear();
    string message;
    var first=new GraphEditRequest{RequestId=Guid.NewGuid().ToString("N"),TopicId=graph.Id,ExpectedRevision="",Title=graph.Title,Architecture=map};
    if(!service.ApplyGraphEdit(service.Slot,first,out message))throw new Exception(message);
    foreach(var module in graph.Architecture.Modules){
     var current=service.Slot.DomainGraphs.Find(g=>g.Id==graph.Id);
     var nodes=graph.Nodes.FindAll(n=>module.Nodes.Contains(n.Id));
     var patch=new GraphEditRequest{RequestId=Guid.NewGuid().ToString("N"),TopicId=graph.Id,ExpectedRevision=GraphEditing.Revision(current),Nodes=nodes,NodeModules=nodes.ConvertAll(n=>new GraphNodeModule{NodeId=n.Id,ModuleId=module.Id})};
     if(!service.ApplyGraphEdit(service.Slot,patch,out message))throw new Exception(module.Id+": "+message);
    }
    for(int i=0;i<graph.Edges.Count;i+=100){
     var current=service.Slot.DomainGraphs.Find(g=>g.Id==graph.Id);
     var patch=new GraphEditRequest{RequestId=Guid.NewGuid().ToString("N"),TopicId=graph.Id,ExpectedRevision=GraphEditing.Revision(current),Edges=graph.Edges.GetRange(i,Math.Min(100,graph.Edges.Count-i))};
     if(!service.ApplyGraphEdit(service.Slot,patch,out message))throw new Exception(message);
    }
    var final=service.Slot.DomainGraphs.Find(g=>g.Id==graph.Id);
    if(final.Nodes.Count!=graph.Nodes.Count||final.Architecture.Modules.Count!=graph.Architecture.Modules.Count)throw new Exception("Import count mismatch");
    foreach(var slot in service.Book.Slots)foreach(var g in slot.DomainGraphs){string prior;if(preserved.TryGetValue(slot.Id+"/"+g.Id,out prior)){
     // DomainKnowledge.List refreshes this derived field on every edit. Restore
     // it before comparing/persisting the otherwise untouched historical graph.
     g.Revision=GraphEditing.Parse<DomainGraph>(prior).Revision;
     if(prior!=GraphEditing.Json(g))throw new Exception("Existing graph changed during import");
    }}
    File.WriteAllText(Path.Combine(folder,"import-validation.json"),GraphEditing.Json(final));
    if(args.Length<2||args[1]!="--apply"){Console.WriteLine("Validated without persistence");return 0;}
    if(Process.GetProcessesByName("ElymsPet").Length>0)throw new Exception("Pet started during validation");
    File.Copy(ArchiveStore.FilePath,Path.Combine(folder,"archive-before-import.json"),false);
    repo.Commit=true;if(!service.Save())throw new Exception(service.Status);
    var saved=ArchiveStore.Load();var active=ArchiveStore.Active(saved);var persisted=active.DomainGraphs.Find(g=>g.Id==graph.Id);
    if(persisted==null||GraphEditing.Json(persisted)!=GraphEditing.Json(final))throw new Exception("Persisted graph differs");
    ArchiveKnowledge.WriteFiles(active);
    File.WriteAllText(Path.Combine(folder,"import-receipt.json"),GraphEditing.Json(new Receipt{Status="applied",TopicId=graph.Id,SlotId=active.Id,Modules=final.Architecture.Modules.Count,Nodes=final.Nodes.Count,Revision=GraphEditing.Revision(persisted)}));
    Console.WriteLine("Applied and read back "+final.Nodes.Count+" nodes in "+final.Architecture.Modules.Count+" modules");
   }
   return 0;
  }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
 }
}
