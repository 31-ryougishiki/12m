using System;
using System.Collections.Generic;
using System.Windows;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel {
  int flowColumns;
  int FlowColumns {get{return Math.Max(1,Math.Min(4,(int)((viewport.ActualWidth-80)/360)));}}
  // Fold only a complete, unbranched chain. Branching graphs retain the general graph layout.
  bool TryFoldModuleFlow(KnowledgeGraphView view){
   if(view.Nodes.Count<6||view.Links.Count!=view.Nodes.Count-1)return false;
   var incoming=new HashSet<string>();var outgoing=new Dictionary<string,KnowledgeLink>();
   foreach(var edge in view.Links){if(!incoming.Add(edge.To)||outgoing.ContainsKey(edge.From))return false;outgoing.Add(edge.From,edge);}
   var first=view.Nodes.Find(n=>!incoming.Contains(n.Id));if(first==null)return false;
   var order=new List<ArchiveItem>();var seen=new HashSet<string>();var item=first;
   while(item!=null&&seen.Add(item.Id)){order.Add(item);KnowledgeLink edge;if(!outgoing.TryGetValue(item.Id,out edge))break;item=view.Nodes.Find(n=>n.Id==edge.To);}
   if(order.Count!=view.Nodes.Count)return false;
   flowColumns=FlowColumns;scene=new KnowledgeGraphScene();var positions=new Dictionary<string,GraphNodePlacement>();
   for(int i=0;i<order.Count;i++){int row=i/flowColumns,col=i%flowColumns;if(row%2==1)col=flowColumns-1-col;var p=Placement(order[i].Id,40+col*360,70+row*270);scene.Nodes.Add(p);positions[p.Item.Id]=p;}
   foreach(var edge in view.Links){var a=positions[edge.From];var b=positions[edge.To];var link=new GraphEdgePlacement{Link=edge};
    if(a.Y==b.Y){bool right=b.X>a.X;link.Points.Add(new Point(a.X+(right?280:0),a.Y+71));link.Points.Add(new Point(b.X+(right?0:280),b.Y+71));link.Label=new Point((a.X+b.X)/2+140,a.Y+158);}
    else{link.Points.Add(new Point(a.X+140,a.Y+142));link.Points.Add(new Point(b.X+140,b.Y));link.Label=new Point(a.X+235,a.Y+185);}
    scene.Edges.Add(link);
   }
   scene.Width=80+flowColumns*360;scene.Height=90+Math.Ceiling(order.Count/(double)flowColumns)*270;return true;
  }
 }
}
