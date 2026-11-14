using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
namespace ElymsPet {
 static class KnowledgeGraphTests {
  static int checks;
  static void Check(bool value,string name){if(!value)throw new Exception("Graph: "+name);checks++;Console.WriteLine("PASS graph: "+name);}
  public static void Run(string root) {
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"graph");
   try {
    var book=ArchiveStore.Normalize(new ArchiveBook());var slot=ArchiveStore.Active(book);slot.Links=null;ArchiveStore.Normalize(book);
    Check(slot.Links!=null&&book.Version==4,"old archives initialize graph without fabricated links");
    var feedback=new PetFeedback(delegate {},delegate {},delegate {});
    using(var service=new ArchiveService(new PetState(),new NoNetwork(),feedback,new TransientRepository<ArchiveBook>(book),true)) {
     service.SaveKnowledgeNode("","注意力","模型结构","已掌握","会用","我看过实现","示例");
     service.SaveKnowledgeNode("","矩阵乘法","模型结构","正在学习","了解","正在学","示例");
     service.SaveKnowledgeNode("","矩阵乘法","数学","待核实","了解","尚未核实","示例");
     string a=slot.Items[0].Id,b=slot.Items[1].Id,c=slot.Items[2].Id;
     Check(!service.SaveKnowledgeNode("","注意力","模型结构","已掌握","会用","重复",""),"duplicate node rejected within domain");
     Check(!service.SaveKnowledgeLink(a,a,"依赖","自环")&&!service.SaveKnowledgeLink(a,"missing","依赖","不存在"),"self-links and foreign endpoints rejected");
     Check(!service.SaveKnowledgeLink(a,b,"依赖","  "),"relationship requires evidence");
     Check(service.SaveKnowledgeLink(a,b,"依赖","我认为注意力的计算依赖矩阵乘法")&&slot.Links[0].Confirmed,"manual relationship saved as explicitly confirmed");
     service.SaveKnowledgeLink(a,b,"依赖","进一步说明");Check(slot.Links.Count==1,"duplicate updates one existing edge");
     service.SaveKnowledgeLink(b,c,"相关","两个领域中的同名概念待比较");service.SaveKnowledgeLink(c,b,"相关","双向相关只保留一条");Check(slot.Links.Count==2,"symmetric links deduplicate reversed endpoints");
     string quote="在我的实现中，注意力包含矩阵乘法这一步。";ArchiveStore.AddTurn(slot,"answer",quote);
     var parsed=ArchiveInterview.Parse("{\"links\":[{\"from\":\"注意力\",\"from_domain\":\"模型结构\",\"to\":\"矩阵乘法\",\"to_domain\":\"模型结构\",\"kind\":\"包含\",\"evidence\":\""+quote+"\",\"confirmed\":true}]}");
     KnowledgeGraph.Merge(slot,parsed.Links);var ai=KnowledgeGraph.Find(slot,a,b,"包含");Check(ai!=null&&!ai.Confirmed,"API relationship grounded but never auto-confirmed");
     int count=slot.Links.Count;
     KnowledgeGraph.Merge(slot,new[]{new KnowledgeLinkProposal{From="注意力",To="矩阵乘法",Kind="应用于",Evidence=quote},new KnowledgeLinkProposal{From=a,To=c,Kind="应用于",Evidence="不是用户原话"}}.ToList());
     Check(slot.Links.Count==count,"ambiguous titles and unsupported evidence rejected");
     Check(!KnowledgeGraph.Context(slot,"注意力",false).Contains("→ 包含 →"),"pending relationships excluded from ordinary recall");
     service.ConfirmKnowledgeLink(ai);Check(KnowledgeGraph.Context(slot,"注意力",false).Contains("→ 包含 →"),"confirmed relationship becomes reusable");
     var domainView=KnowledgeGraph.Filter(slot,"模型结构","","","",80);
     Check(domainView.Nodes.Count==2&&domainView.Links.Count==2,"domain filter preserves only visible endpoints");
     Check(KnowledgeGraph.Filter(slot,"","待核实","","",80).Nodes.Count==1,"knowledge boundary filter");
     Check(KnowledgeGraph.Filter(slot,"","","注意力","",80).Nodes.Count==1,"keyword search");
     Check(KnowledgeGraph.Filter(slot,"","","",a,80).Nodes.Count==2,"one-hop neighborhood");
     var limited=KnowledgeGraph.Filter(slot,"","","","",1);Check(limited.Nodes.Count==1&&limited.MatchingCount==3&&limited.Links.Count==0,"display limit reports total without dangling edges");
     service.SaveKnowledgeNode(a,"注意力机制","模型结构","已掌握","会用","更新名称","");Check(!ai.Confirmed&&slot.Items[0].Id==a,"rename keeps identity and invalidates attached relation confirmations");
     Check(ArchiveStore.Save(book),"graph persisted");var loaded=ArchiveStore.Active(ArchiveStore.Load());Check(loaded.Links.Count==3&&loaded.Items.Count==3&&loaded.Links[0].Evidence=="进一步说明","graph survives restart with evidence and status");
     ArchiveKnowledge.WriteFiles(slot);string path=Path.Combine(ArchiveKnowledge.Folder(slot),"graph.json");Check(File.Exists(path)&&File.ReadAllText(path).Contains("Links"),"full graph exported as structured local JSON");
     // Include a directed cycle and a disconnected node to exercise the real MSAGL engine.
     service.SaveKnowledgeLink(b,a,"应用于","反向应用示例");
     var scene=KnowledgeGraphLayout.Calculate(KnowledgeGraph.Filter(slot,"","","","",80));
     Check(scene.Nodes.Count==3&&scene.Edges.Count==4&&scene.Width>0&&scene.Height>0,"MSAGL handles directed cycles and multiple edges");
     foreach(var node in scene.Nodes)Check(!double.IsNaN(node.X)&&!double.IsInfinity(node.Y),"finite node placement");
     service.DeleteItem(slot.Items[1]);Check(slot.Links.Count==0&&slot.Items.Count==2,"deleting node removes incident links and preserves other nodes");
     service.ClearProfile();Check(slot.Links.Count==0&&slot.Items.Count==0,"clear profile clears graph");
    }
    var many=ArchiveStore.NewSlot("large");for(int i=0;i<100;i++)ArchiveStore.Merge(many,new[]{new ArchiveItem{Title="知识"+i,Domain="测试"}});
    var snapshot=KnowledgeGraph.Filter(many,"","","","",80);var large=KnowledgeGraphLayout.Calculate(snapshot);
    Check(large.Width<750&&large.Nodes[2].Y>large.Nodes[0].Y,"unlinked knowledge wraps into readable rows instead of a horizontal strip");
    Check(snapshot.MatchingCount==100&&large.Nodes.Count==80,"bounded layout supports large archives without deleting knowledge");
    Check(ArchiveInterview.BuildSystem(slot).Contains("links=")&&ArchiveInterview.BuildExtract(slot)[0].Content.Contains("links="),"both extraction paths provide relationship schema");
    Console.WriteLine("PASS graph: "+checks+" checks");
   } finally {Storage.Folder=previous;}
  }
  static List<T> ToList<T>(this T[] array){return new List<T>(array);}
  sealed class NoNetwork:IWorkScheduler {public void Queue(Action action){throw new Exception("Unexpected API call");}public void Post(Action action){action();}}
 }
}
