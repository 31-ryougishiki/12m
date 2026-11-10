using System;
using System.Collections.Generic;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Layered;
using GraphPoint=Microsoft.Msagl.Core.Geometry.Point;
namespace ElymsPet {
 public sealed class GraphNodePlacement {public ArchiveItem Item;public double X,Y;}
 public sealed class GraphEdgePlacement {public KnowledgeLink Link;public List<System.Windows.Point> Points=new List<System.Windows.Point>();public System.Windows.Point Label;}
 public sealed class KnowledgeGraphScene {
  public List<GraphNodePlacement> Nodes=new List<GraphNodePlacement>();
  public List<GraphEdgePlacement> Edges=new List<GraphEdgePlacement>();
  public double Width=640,Height=400;
 }
 public static class KnowledgeGraphLayout {
  public const double NodeWidth=180,NodeHeight=76;
  public static KnowledgeGraphScene Calculate(KnowledgeGraphView view) {
   var scene=new KnowledgeGraphScene();if(view.Nodes.Count==0)return scene;
   var graph=new GeometryGraph();var nodes=new Dictionary<string,Node>();
   foreach(var item in view.Nodes){var node=new Node(CurveFactory.CreateRectangle(NodeWidth,NodeHeight,new GraphPoint()));nodes[item.Id]=node;graph.Nodes.Add(node);}
   foreach(var link in view.Links)if(nodes.ContainsKey(link.From)&&nodes.ContainsKey(link.To))graph.Edges.Add(new Edge(nodes[link.From],nodes[link.To],76,22,1){UserData=link});
   var settings=new SugiyamaLayoutSettings{LayerSeparation=75,NodeSeparation=40};
   new LayeredLayout(graph,settings).Run();
   scene.Width=Math.Max(640,graph.Width+80);scene.Height=Math.Max(400,graph.Height+80);
   foreach(var item in view.Nodes){var center=nodes[item.Id].Center;scene.Nodes.Add(new GraphNodePlacement{Item=item,X=center.X-graph.Left+40-NodeWidth/2,Y=graph.Top-center.Y+40-NodeHeight/2});}
   foreach(var edge in graph.Edges) {
    var placement=new GraphEdgePlacement{Link=(KnowledgeLink)edge.UserData};
    if(edge.Curve!=null)for(int n=0;n<=48;n++){double t=edge.Curve.ParStart+(edge.Curve.ParEnd-edge.Curve.ParStart)*n/48;var point=edge.Curve[t];placement.Points.Add(new System.Windows.Point(point.X-graph.Left+40,graph.Top-point.Y+40));}
    else {placement.Points.Add(new System.Windows.Point(edge.Source.Center.X-graph.Left+40,graph.Top-edge.Source.Center.Y+40));placement.Points.Add(new System.Windows.Point(edge.Target.Center.X-graph.Left+40,graph.Top-edge.Target.Center.Y+40));}
    var label=edge.Label.Center;placement.Label=new System.Windows.Point(label.X-graph.Left+40,graph.Top-label.Y+40);
    scene.Edges.Add(placement);
   }
   return scene;
  }
 }
}
