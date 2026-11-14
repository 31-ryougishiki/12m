using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.IO;
namespace ElymsPet {
 [DataContract] public sealed class DomainNode {
  [DataMember] public string Id="",Title="",Tier="A",Summary="",Approach="",Code="",Sources="",Status="参考资料",Version="";
  [DataMember(EmitDefaultValue=false)] public NodeLearning Learning;
  [DataMember(EmitDefaultValue=false)] public NodeReadingGuide ReadingGuide;
 }
 [DataContract] public sealed class DomainEdge {
  [DataMember] public string From="",To="",Label="相关",Evidence="";
 }
 [DataContract] public sealed class DomainGraph {
  [DataMember] public string Id="",Title="",Description="";
  [DataMember] public string Revision="";
  [DataMember] public List<DomainNode> Nodes=new List<DomainNode>();
  [DataMember] public List<DomainEdge> Edges=new List<DomainEdge>();
  [DataMember(EmitDefaultValue=false)] public RepositoryMap Architecture;
  public override string ToString(){return Title;}
 }
 public static class DomainKnowledge {
  public static readonly string[] Tiers={"CORE","S","A","B"};
  public static string TierName(string tier){return tier=="CORE"?"核心 · 高频":tier=="S"?"S · 复杂特性":tier=="B"?"B · 辅助 / 历史":"A · 基础结构";}
  public static DomainGraph Clone(DomainGraph graph){var s=new DataContractJsonSerializer(typeof(DomainGraph));using(var m=new MemoryStream()){s.WriteObject(m,graph);m.Position=0;return (DomainGraph)s.ReadObject(m);}}
  public static void Normalize(ArchiveSlot slot){
   if(slot.AppliedGraphEdits==null)slot.AppliedGraphEdits=new List<string>();
   if(slot.DomainGraphs==null)slot.DomainGraphs=new List<DomainGraph>();
   slot.DomainGraphs.RemoveAll(delegate(DomainGraph g){return g==null||string.IsNullOrWhiteSpace(g.Id)||string.IsNullOrWhiteSpace(g.Title);});
   var graphIds=new HashSet<string>();slot.DomainGraphs.RemoveAll(delegate(DomainGraph g){return !graphIds.Add(g.Id);});
   foreach(var g in slot.DomainGraphs){
    if(g.Nodes==null)g.Nodes=new List<DomainNode>();if(g.Edges==null)g.Edges=new List<DomainEdge>();
    var ids=new HashSet<string>();g.Nodes.RemoveAll(delegate(DomainNode n){return n==null||string.IsNullOrWhiteSpace(n.Id)||!ids.Add(n.Id);});
    foreach(var n in g.Nodes){if(Array.IndexOf(Tiers,n.Tier)<0)n.Tier="A";n.Title=n.Title??"未命名";n.Summary=n.Summary??"";n.Approach=n.Approach??"";n.Code=n.Code??"";n.Sources=n.Sources??"";n.Status=n.Status??"待补充";n.Version=n.Version??"";}
    g.Edges.RemoveAll(delegate(DomainEdge e){return e==null||e.From==e.To||!ids.Contains(e.From??"")||!ids.Contains(e.To??"");});
   }
  }
  public static List<DomainGraph> List(ArchiveSlot slot){
   Normalize(slot);var result=new List<DomainGraph>(slot.DomainGraphs);
   var domains=new Dictionary<string,DomainGraph>();
   foreach(var i in slot.Items){string domain=string.IsNullOrWhiteSpace(i.Domain)?"未分类":i.Domain;DomainGraph g;
    if(!domains.TryGetValue(domain,out g)){g=new DomainGraph{Id="archive:"+domain,Title=domain+" · 我的记录",Description="访谈中积累的知识；等级默认为 A，可编辑专题补充内容。"};domains.Add(domain,g);}
    g.Nodes.Add(new DomainNode{Id=i.Id,Title=i.Title,Tier="A",Summary=i.Note,Approach=i.Evidence,Status=i.Boundary+" · "+i.Level,Sources=i.Source,Version="个人存档"});
   }
   foreach(var g in domains.Values){
    foreach(var e in slot.Links)if(g.Nodes.Exists(delegate(DomainNode n){return n.Id==e.From;})&&g.Nodes.Exists(delegate(DomainNode n){return n.Id==e.To;}))g.Edges.Add(new DomainEdge{From=e.From,To=e.To,Label=e.Kind+(e.Confirmed?"":" · 待核实"),Evidence=e.Evidence});
    int index=result.FindIndex(delegate(DomainGraph x){return x.Id==g.Id;});
    if(index<0){result.Add(g);continue;}
    var merged=Clone(result[index]);foreach(var n in g.Nodes)if(!merged.Nodes.Exists(delegate(DomainNode x){return x.Id==n.Id;}))merged.Nodes.Add(n);
    foreach(var e in g.Edges)if(!merged.Edges.Exists(delegate(DomainEdge x){return x.From==e.From&&x.To==e.To&&x.Label==e.Label;}))merged.Edges.Add(e);result[index]=merged;
   }foreach(var g in result)g.Revision=GraphEditing.Revision(g);return result;
  }

 }
 public sealed partial class ArchiveService {
  public bool SaveDomainGraph(DomainGraph graph){
   if(!PluginAccess.Enabled(state,"graph")||busy||graph==null||string.IsNullOrWhiteSpace(graph.Id)||string.IsNullOrWhiteSpace(graph.Title))return false;
   var previous=new List<DomainGraph>(slot.DomainGraphs);var updated=slot.Updated;
   slot.DomainGraphs.RemoveAll(delegate(DomainGraph g){return g.Id==graph.Id;});slot.DomainGraphs.Add(DomainKnowledge.Clone(graph));slot.Updated=DateTime.UtcNow;
   if(!Save()){slot.DomainGraphs=previous;slot.Updated=updated;return false;}
   profileRevision++;Notify("专题内容已保存。");return true;
  }
 }
}
