using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
namespace ElymsPet {
 [DataContract] public sealed class KnowledgeLink {
  [DataMember] public string Id="";
  [DataMember] public string From="";
  [DataMember] public string To="";
  [DataMember] public string Kind="相关";
  [DataMember] public string Evidence="";
  [DataMember] public bool Confirmed;
  [DataMember] public string Source="访谈提取";
  [DataMember] public DateTime At;
 }
 public sealed class KnowledgeLinkProposal {
  public string From="",To="",FromDomain="",ToDomain="",Kind="",Evidence="";
 }
 [DataContract] public sealed class KnowledgeGraphExport {
  [DataMember] public int Version=1;
  [DataMember] public string Archive="";
  [DataMember] public List<ArchiveItem> Nodes=new List<ArchiveItem>();
  [DataMember] public List<KnowledgeLink> Links=new List<KnowledgeLink>();
  [DataMember] public List<DomainGraph> Topics=new List<DomainGraph>();
 }
 public sealed class KnowledgeGraphView {
  public List<ArchiveItem> Nodes=new List<ArchiveItem>();
  public List<KnowledgeLink> Links=new List<KnowledgeLink>();
  public int MatchingCount;
 }
 public static class KnowledgeGraph {
  public static readonly string[] Kinds={"包含","依赖","应用于","对比","相关"};
  public const string Schema="\n知识图谱字段 links=[{from,from_domain,to,to_domain,kind,evidence}]，from/to 为知识条目的精确标题（也可用提供的 ID），领域用于消歧；kind 只能是包含|依赖|应用于|对比|相关。方向按‘from 依赖 to’等读法。只提取用户明确描述的知识关系，evidence 必须逐字引用用户原话。不要用同领域、共同出现或常识猜测关系；不得为了连线编造知识条目，缺少端点就先追问。关系均需用户确认。\n";
  public static bool ValidKind(string kind) {return Array.IndexOf(Kinds,kind)>=0;}
  public static KnowledgeLink Copy(KnowledgeLink x) {return new KnowledgeLink{Id=x.Id,From=x.From,To=x.To,Kind=x.Kind,Evidence=x.Evidence,Confirmed=x.Confirmed,Source=x.Source,At=x.At};}
  public static void Normalize(ArchiveSlot slot) {
   if(slot.Links==null)slot.Links=new List<KnowledgeLink>();
   var ids=new HashSet<string>();if(slot.Items!=null)foreach(var item in slot.Items)if(item!=null)ids.Add(item.Id??"");
   var keys=new HashSet<string>();var linkIds=new HashSet<string>();var links=new List<KnowledgeLink>();
   foreach(var link in slot.Links) {
    if(link==null||link.From==link.To||!ids.Contains(link.From??"")||!ids.Contains(link.To??"")||!ValidKind(link.Kind))continue;
    string key=Key(link.From,link.To,link.Kind);if(!keys.Add(key))continue;
    if(string.IsNullOrEmpty(link.Id)||!linkIds.Add(link.Id)){link.Id=Guid.NewGuid().ToString("N");linkIds.Add(link.Id);}
    link.Evidence=ArchiveStore.Clip(ArchiveStore.Clean(link.Evidence),500);
    link.Source=link.Source=="手动关联"?link.Source:"访谈提取";
    if(link.At==DateTime.MinValue)link.At=DateTime.UtcNow;
    links.Add(link);if(links.Count>=1000)break;
   }
   slot.Links=links;
  }
  static string Key(string from,string to,string kind) {
   if((kind=="相关"||kind=="对比")&&string.CompareOrdinal(from,to)>0){string swap=from;from=to;to=swap;}
   return from+"|"+kind+"|"+to;
  }
  public static KnowledgeLink Find(ArchiveSlot slot,string from,string to,string kind) {
   return slot.Links.Find(delegate(KnowledgeLink x){return Key(x.From,x.To,x.Kind)==Key(from,to,kind);});
  }
  public static void Parse(Dictionary<string,object> map,InterviewResult result) {
   var list=MiniJson.GetArray(map,"links");if(list==null)return;
   foreach(var value in list) {
    var x=value as Dictionary<string,object>;if(x==null)continue;
    result.Links.Add(new KnowledgeLinkProposal{From=MiniJson.GetString(x,"from"),To=MiniJson.GetString(x,"to"),FromDomain=MiniJson.GetString(x,"from_domain"),ToDomain=MiniJson.GetString(x,"to_domain"),Kind=MiniJson.GetString(x,"kind"),Evidence=MiniJson.GetString(x,"evidence")});
   }
  }
  static ArchiveItem Resolve(ArchiveSlot slot,string value,string domain) {
   var exact=slot.Items.Find(delegate(ArchiveItem i){return i.Id==value;});if(exact!=null)return exact;
   var matches=slot.Items.FindAll(delegate(ArchiveItem i){return string.Equals(i.Title,ArchiveStore.Clean(value),StringComparison.OrdinalIgnoreCase)&&(string.IsNullOrEmpty(domain)||string.Equals(i.Domain,domain,StringComparison.OrdinalIgnoreCase));});
   return matches.Count==1?matches[0]:null;
  }
  public static void Merge(ArchiveSlot slot,List<KnowledgeLinkProposal> proposals) {
   Normalize(slot);if(proposals==null)return;
   foreach(var p in proposals) {
    if(p==null||!ValidKind(p.Kind)||slot.Links.Count>=1000)continue;
    var from=Resolve(slot,p.From,p.FromDomain);var to=Resolve(slot,p.To,p.ToDomain);
    string evidence=ArchiveStore.Clean(p.Evidence);if(from==null||to==null||from.Id==to.Id||evidence.Length==0||evidence.Length>500)continue;
    bool grounded=slot.Turns.Exists(delegate(ArchiveTurn t){return t.Role=="answer"&&t.Text.Contains(evidence);});
    if(!grounded||Find(slot,from.Id,to.Id,p.Kind)!=null)continue;
    slot.Links.Add(new KnowledgeLink{Id=Guid.NewGuid().ToString("N"),From=from.Id,To=to.Id,Kind=p.Kind,Evidence=evidence,At=DateTime.UtcNow});
   }
  }
  public static KnowledgeGraphView Filter(ArchiveSlot slot,string domain,string boundary,string query,string neighbor,int limit) {
   Normalize(slot);var result=new KnowledgeGraphView();var adjacent=new HashSet<string>();
   if(!string.IsNullOrEmpty(neighbor)){adjacent.Add(neighbor);foreach(var e in slot.Links)if(e.From==neighbor||e.To==neighbor){adjacent.Add(e.From);adjacent.Add(e.To);}}
   query=ArchiveStore.Clean(query);
   foreach(var i in slot.Items) {
    if(!string.IsNullOrEmpty(domain)&&i.Domain!=domain)continue;
    if(!string.IsNullOrEmpty(boundary)&&i.Boundary!=boundary)continue;
    if(adjacent.Count>0&&!adjacent.Contains(i.Id))continue;
    if(query.Length>0&&(i.Title+" "+i.Domain+" "+i.Note).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
    result.MatchingCount++;if(limit<=0||result.Nodes.Count<limit)result.Nodes.Add(ArchiveLearning.Copy(i));
   }
   var visible=new HashSet<string>();foreach(var i in result.Nodes)visible.Add(i.Id);
   foreach(var e in slot.Links)if(visible.Contains(e.From)&&visible.Contains(e.To))result.Links.Add(Copy(e));
   return result;
  }
  public static string Describe(ArchiveSlot slot,KnowledgeLink link) {
   var from=slot.Items.Find(delegate(ArchiveItem i){return i.Id==link.From;});var to=slot.Items.Find(delegate(ArchiveItem i){return i.Id==link.To;});
   return (from==null?"?":from.Title)+" → "+link.Kind+" → "+(to==null?"?":to.Title);
  }
  public static string Context(ArchiveSlot slot,string query,bool includePending) {
   Normalize(slot);var text=new StringBuilder();int count=0;
   foreach(var e in slot.Links) {
    if(!e.Confirmed&&!includePending)continue;
    string description=Describe(slot,e);
    if(!string.IsNullOrEmpty(query)&&ArchiveRecall.Score(query,description)<=0)continue;
    text.Append(e.Confirmed?"已确认关系：":"待核实关系（不要当作事实）：").Append(description).Append("；依据：").Append(e.Evidence).Append("\n");
    if(++count>=12)break;
   }
   return text.ToString();
  }
  public static KnowledgeGraphExport Export(ArchiveSlot slot) {Normalize(slot);return new KnowledgeGraphExport{Version=2,Archive=slot.Name,Nodes=slot.Items,Links=slot.Links,Topics=DomainKnowledge.List(slot)};}
 }
}
