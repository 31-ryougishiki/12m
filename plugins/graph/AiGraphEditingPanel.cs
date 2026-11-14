using System;
using System.Windows;
using System.Windows.Controls;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel {
  readonly WrapPanel graphEditResults=new WrapPanel{Visibility=Visibility.Collapsed};
  readonly ScrollViewer graphEditResultsHost=new ScrollViewer{MaxHeight=96,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  GraphEditOutcome lastGraphEdit;string lastGraphInstruction="";
  void RequestAiGraphEdit(DomainNode node,RepositoryModule module){
   if(graph==null)return;if(archive.Busy){status.Text="请等待当前任务完成后再委托。";return;}
   string topic=graph.Id,slot=archive.Slot.Id,nodeId=node==null?"":node.Id;
   if(module==null&&node!=null)module=RepositoryArchitecture.Parent(architecture,node.Id);
   string moduleId=module==null?"":module.Id;
   string context=graph.Title+(module==null?"":" / "+RepositoryArchitecture.PathLabel(architecture,module.Id))+(node==null?"":" / "+node.Title);
   string request=DomainGraphEditor.Prompt(LearningOwner,context,lastGraphInstruction);if(request==null)return;
   if(archive.Slot.Id!=slot||ActiveDomain!=topic){status.Text="领域已切换，请在目标图谱重新委托。";return;}
   lastGraphInstruction=request;RememberReflection();archive.CurrentDomainId=topic;
   archive.EditGraphWithAi(request,topic,nodeId,moduleId,delegate(GraphEditOutcome result){
    if(disposed)return;if(result.Success)lastGraphInstruction="";PresentGraphEdit(result);
   });
  }
  public void PresentGraphEdit(GraphEditOutcome result){
   if(disposed||result==null||archive.Slot.Id!=result.SlotId||ActiveDomain!=result.TopicId)return;
   status.Text=result.Message;if(!result.Success)return;lastGraphEdit=result;RefreshGraphEditResults();
   if(result.AddedNodeIds.Count>0)LocateGraphEditNode(result.AddedNodeIds[0]);else if(result.UpdatedModuleIds.Count>0)LocateGraphEditModule(result.UpdatedModuleIds[0]);
  }
  void RefreshGraphEditResults(){
   graphEditResults.Children.Clear();graphEditResults.Visibility=Visibility.Collapsed;
   if(lastGraphEdit==null||!lastGraphEdit.Success||graph==null||lastGraphEdit.SlotId!=archive.Slot.Id||lastGraphEdit.TopicId!=graph.Id||(lastGraphEdit.AddedNodeIds.Count==0&&lastGraphEdit.UpdatedModuleIds.Count==0))return;
   graphEditResults.Visibility=Visibility.Visible;graphEditResults.Children.Add(Text(lastGraphEdit.UpdatedModuleIds.Count>0?"本次整理 · ":"本次新增 · ",13,"#247A7B"));
   foreach(string key in lastGraphEdit.UpdatedModuleIds){string id=key;var module=architecture==null?null:architecture.Modules.Find(m=>m.Id==id);if(module==null)continue;var link=Link("模块 · "+module.Title,delegate{LocateGraphEditModule(id);});link.MaxWidth=260;link.TextWrapping=TextWrapping.NoWrap;link.TextTrimming=TextTrimming.CharacterEllipsis;link.ToolTip=module.Title;link.Margin=new Thickness(0,0,16,6);graphEditResults.Children.Add(link);}
   foreach(string key in lastGraphEdit.AddedNodeIds){string id=key;var node=graph.Nodes.Find(delegate(DomainNode n){return n.Id==id;});if(node==null)continue;var link=Link(node.Title,delegate{LocateGraphEditNode(id);});link.Margin=new Thickness(0,0,16,6);graphEditResults.Children.Add(link);}
  }
  void LocateGraphEditModule(string id){if(architecture==null)return;var module=architecture.Modules.Find(m=>m.Id==id);if(module==null)return;if(string.IsNullOrEmpty(module.ParentId))ShowArchitecture("overview");else OpenArchitectureModule(module.ParentId);SelectNode(ModuleKey(id));CenterSelected();}
  void LocateGraphEditNode(string id){
   if(graph==null||!graph.Nodes.Exists(delegate(DomainNode n){return n.Id==id;}))return;
   RememberReflection();search.Clear();detailTab=0;
   if(architecture!=null){architectureMode="overview";var module=RepositoryArchitecture.Parent(architecture,id);if(SingleModuleNode(module)!=null){OpenArchitectureModule(module.Id);canvas.UpdateLayout();CenterSelected();RequestFullscreenPosition();return;}architectureModule=module==null?"":module.Id;UpdateArchitectureBar();}
   selected=id;fullDetailsVisible=true;Populate();Rebuild();canvas.UpdateLayout();CenterSelected();RequestFullscreenPosition();
  }
 }
}
