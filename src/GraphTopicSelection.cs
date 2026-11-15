using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
namespace ElymsPet {
 [DataContract] public sealed class GraphTopicPreferences {
  [DataMember] public Dictionary<string,string> Topics=new Dictionary<string,string>();
 }
 public static class GraphTopicSelection {
  static string FilePath {get{return Path.Combine(Storage.Folder,"graph-view.json");}}
  static GraphTopicPreferences Load(){string error;var value=JsonFile.Load<GraphTopicPreferences>(FilePath,out error)??new GraphTopicPreferences();if(value.Topics==null)value.Topics=new Dictionary<string,string>();return value;}
  public static void Remember(string slot,string topic){var value=Load();value.Topics[slot]=topic;string error;JsonFile.Save(FilePath,value,Storage.Folder,out error);}
  public static DomainGraph Select(ArchiveSlot slot,List<DomainGraph> graphs,string current,bool usePreferences=true){
   string remembered="";if(usePreferences)Load().Topics.TryGetValue(slot.Id,out remembered);
   var chosen=graphs.Find(g=>g.Id==current)??graphs.Find(g=>g.Id==remembered);
   if(chosen!=null)return chosen;
   // Explicit topics are stored in creation/save order; derived interview topics follow them.
   for(int i=slot.DomainGraphs.Count-1;i>=0;i--){chosen=graphs.Find(g=>g.Id==slot.DomainGraphs[i].Id);if(chosen!=null)return chosen;}
   return graphs.Count>0?graphs[graphs.Count-1]:null;
  }
 }
 public sealed partial class ArchiveService {
  public void RememberGraphTopic(string id){if(!preview)GraphTopicSelection.Remember(slot.Id,id);}
  public DomainGraph SelectGraphTopic(List<DomainGraph> graphs,string current){return GraphTopicSelection.Select(slot,graphs,current,!preview);}
 }
}
