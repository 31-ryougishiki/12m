using System;
using System.Windows;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel {
  public GraphReadingView CaptureReadingView(){
   if(disposed||graph==null||(!IsFullscreen&&!Root.IsVisible))return null;
   var module=SelectedModule();
   var view=new GraphReadingView{SlotId=slotId,TopicId=graph.Id,Mode=architecture==null?"relations":architectureMode=="overview"&&architectureModule.Length>0?"module":architectureMode,ModuleId=module==null?architectureModule:module.Id,NodeId=Selected()==null?"":Selected().Id,Search=search.Text,Fullscreen=IsFullscreen,DetailsVisible=detailFrame.IsVisible,JourneyIndex=architectureMode=="journey"?journeyIndex:-1};
   view.DetailSection=new[]{"概览","代码","学习","资料"}[Math.Max(0,Math.Min(3,detailTab))];if(detailTab==0&&Selected()!=null&&Selected().ReadingGuide!=null)view.DetailSection+=" / "+new[]{"实现原文","术语翻译","流程对照"}[Math.Max(0,Math.Min(2,guideTab))];
   var visible=new Rect(viewport.HorizontalOffset/scale.ScaleX,viewport.VerticalOffset/scale.ScaleY,Math.Max(0,viewport.ViewportWidth/scale.ScaleX),Math.Max(0,viewport.ViewportHeight/scale.ScaleY));
   if(architecture!=null&&architectureMode=="journey"){
    for(int i=0;i<architecture.Journey.Count;i++){Point p;if(modulePositions.TryGetValue("@step/"+i,out p)&&visible.IntersectsWith(new Rect(p.X,p.Y,280,142)))view.Visible.Add("步骤"+(i+1)+":"+architecture.Journey[i].NodeId);}
   }else if(scene!=null)foreach(var p in scene.Nodes)if(visible.IntersectsWith(new Rect(p.X,p.Y,280,142)))view.Visible.Add(p.Item.Id);
   return view;
  }
 }
}
