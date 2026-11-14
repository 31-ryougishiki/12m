using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel:IDisposable {
  readonly ArchiveService archive;readonly Action<string> interview;
  readonly Canvas canvas=new Canvas{Background=Theme.Brush("#F8FAFF")};
  readonly ScrollViewer viewport=new ScrollViewer{Height=540,Background=Brushes.Transparent,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  readonly ScaleTransform scale=new ScaleTransform(1,1);
  readonly ComboBox domains=new ComboBox{Width=240,MinHeight=36,Margin=new Thickness(0,0,12,8)};
  readonly TextBox search=Ui.Input("",36);
  readonly TextBlock heading=Text("",28,"#28324A"),status=Text("",13,"#247A7B");
  readonly Grid graphFrame=new Grid();
  readonly StackPanel inspector=new StackPanel();readonly Grid body=new Grid();readonly Border detailFrame;readonly StackPanel graphArea;
  readonly Button manualEdit,addNode;
  readonly DispatcherTimer searchTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
  DomainGraph graph;KnowledgeGraphScene scene;string selected="",slotId="";int revision=-1,generation;
  bool refreshing,disposed,panning,wide;Point panStart;double panX,panY;
  public UIElement Root {get;private set;}
  public bool IsLayoutReady {get;private set;}
  public string ActiveDomain {get{return graph==null?"":graph.Id;}}
  static TextBlock Text(string value,double size,string color){return new GraphCopyText{Text=value,FontFamily=size>=18?Theme.Font:Theme.ReadingFont,FontSize=size,Foreground=Theme.Brush(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};}
  public static string Color(string tier){return tier=="CORE"?"#008B83":tier=="S"?"#A56700":tier=="B"?"#2168AD":"#8453BF";}
  static Brush Accent(string tier){if(tier!="CORE")return Theme.Brush(tier=="S"?"#FFD476":tier=="A"?"#C597FF":"#72BCFF");var b=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,0)};string[] colors={"#70E9D5","#B99AEC","#F5A1CC","#FFE39B"};for(int i=0;i<4;i++){var color=(System.Windows.Media.Color)ColorConverter.ConvertFromString(colors[i]);b.GradientStops.Add(new GradientStop(color,i/4.0));b.GradientStops.Add(new GradientStop(color,(i+1)/4.0));}b.Freeze();return b;}
  static Button Button(string text,Action action){var b=Ui.Button(text,action,false);b.Background=Theme.Brush("#E8EDF9");b.Foreground=Theme.Brush("#29324B");b.BorderBrush=Theme.Brush("#8391B0");return b;}
  public KnowledgeGraphPanel(ArchiveService archive,Action<string> interview,ChatService conversation=null){
   this.archive=archive;this.interview=interview;InitializeStudyTools(conversation);graphEditResultsHost.Content=graphEditResults;
   search.Width=180;search.Background=Theme.Brush("#E8EDF9");search.Foreground=Theme.Brush("#29324B");search.BorderBrush=Theme.Brush("#8391B0");search.MaxLength=100;search.ToolTip="搜索当前领域的模块、节点标题、介绍、实现思路与代码";
   var gridDrawing=new DrawingGroup();gridDrawing.Children.Add(new GeometryDrawing(Theme.Brush("#F8FAFF"),null,new RectangleGeometry(new Rect(0,0,32,32))));gridDrawing.Children.Add(new GeometryDrawing(Theme.Brush("#D4DCEE"),null,new RectangleGeometry(new Rect(16,16,1,1))));var gridBrush=new DrawingBrush(gridDrawing){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,32,32)};gridBrush.Freeze();canvas.Background=gridBrush;canvas.LayoutTransform=scale;viewport.Content=canvas;
   var toolbar=new Grid{Margin=new Thickness(0,0,0,18)};toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(240)});toolbar.ColumnDefinitions.Add(new ColumnDefinition());toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});domains.Width=double.NaN;domains.Margin=new Thickness(0,0,16,0);domains.ToolTip="切换知识领域";toolbar.Children.Add(domains);var searchBox=ModernControls.Search(search,"搜索模块、节点或代码");searchBox.Margin=new Thickness(0);Grid.SetColumn(searchBox,1);toolbar.Children.Add(searchBox);var create=Button("＋ 新领域",NewDomain);create.Background=Brushes.Transparent;create.BorderThickness=new Thickness(0);create.Foreground=Theme.Brush("#5368B2");create.FontFamily=Theme.ReadingFont;create.FontSize=14;create.Margin=new Thickness(12,0,0,0);Grid.SetColumn(create,2);toolbar.Children.Add(create);
   var fullscreenButton=EditAction("⛶ 全屏学习",EnterFullscreen);fullscreenButton.ToolTip="沉浸式图谱 · 点击节点显示旁侧说明 · Esc 退出";
   var legend=Text("CORE 核心高频  /  S 复杂特性  /  A 基础结构  /  B 辅助与历史",12,"#59647E");
   graphFrame.Children.Add(viewport);var reset=ModernControls.Icon("↺","复原视图",delegate{Zoom(1);CenterSelected();});reset.Margin=new Thickness(0,12,24,0);reset.HorizontalAlignment=HorizontalAlignment.Right;reset.VerticalAlignment=VerticalAlignment.Top;graphFrame.Children.Add(reset);

   var frame=new Border{BorderBrush=Theme.Brush("#8996B5"),BorderThickness=new Thickness(1),Child=graphFrame,ClipToBounds=true};
   graphArea=Ui.Stack(studyTools,architectureBar,graphEditResultsHost,frame,Text("滚轮缩放 · 拖动空白平移 · 单击查看 · 双击编辑节点 · 拖选文字 Ctrl+C · 右键复制全文 · 对话同步当前图谱",12,"#5B6781"));
   manualEdit=EditAction("编辑节点",delegate{var node=Selected();if(node!=null)EditNode(node);});addNode=EditAction("＋ 新增节点",delegate{EditNode(null);});
   var details=new DockPanel{MaxHeight=650};dismissDetails=EditAction("×",delegate{fullDetailsVisible=false;PositionFullscreenDetails();});dismissDetails.ToolTip="收起节点说明";dismissDetails.Visibility=Visibility.Collapsed;var editing=Ui.Row(CreateDetailsGrip(),manualEdit,addNode,dismissDetails);editing.Margin=new Thickness(0,0,0,12);DockPanel.SetDock(editing,Dock.Top);details.Children.Add(editing);details.Children.Add(new ScrollViewer{Content=inspector,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
   detailFrame=new Border{Background=Theme.Brush("#FFFFFF"),Padding=new Thickness(20),BorderBrush=Theme.Brush("#8996B5"),BorderThickness=new Thickness(1),Child=details};
   body.ColumnDefinitions.Add(new ColumnDefinition());body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0)});body.RowDefinitions.Add(new RowDefinition());body.RowDefinitions.Add(new RowDefinition());body.Children.Add(graphArea);body.Children.Add(detailFrame);
   var content=Ui.Stack(Text("ELYMS / KNOWLEDGE ARCHIVE   ▪ ▪ ▪",11,"#247A7B"),toolbar,Ui.Row(fullscreenButton,EditAction("体验 vLLM / Ascend 源码导览",delegate{var example=RepositoryLearningExample.Create();if(archive.SaveDomainGraph(example))SelectDomain(example.Id);})),heading,legend,body,status);
   Root=new Border{Background=Theme.Brush("#F2F4FA"),Padding=new Thickness(22),Child=content,CornerRadius=new CornerRadius(0)};
   ((FrameworkElement)Root).SizeChanged+=delegate{ArrangeBody(((FrameworkElement)Root).ActualWidth>=1100);};ArrangeBody(false);
   domains.SelectionChanged+=delegate{if(!refreshing){graph=domains.SelectedItem as DomainGraph;if(graph!=null)archive.CurrentDomainId=graph.Id;selected=graph!=null&&graph.Nodes.Count>0?graph.Nodes[0].Id:"";search.Clear();Zoom(1);PrepareArchitecture();Populate();Rebuild();}};
   search.TextChanged+=delegate{if(!refreshing){searchTimer.Stop();searchTimer.Start();}};searchTimer.Tick+=delegate{searchTimer.Stop();Rebuild();};
   viewport.PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e){var at=e.GetPosition(viewport);double x=(viewport.HorizontalOffset+at.X)/scale.ScaleX,y=(viewport.VerticalOffset+at.Y)/scale.ScaleY;Zoom(scale.ScaleX*(e.Delta>0?1.15:1/1.15));canvas.UpdateLayout();viewport.ScrollToHorizontalOffset(Math.Max(0,x*scale.ScaleX-at.X));viewport.ScrollToVerticalOffset(Math.Max(0,y*scale.ScaleY-at.Y));e.Handled=true;};
   viewport.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(!GraphBackground(e.OriginalSource as DependencyObject))return;panning=true;panStart=e.GetPosition(viewport);panX=viewport.HorizontalOffset;panY=viewport.VerticalOffset;viewport.CaptureMouse();e.Handled=true;};
   viewport.SizeChanged+=delegate{if(flowColumns>0&&flowColumns!=FlowColumns){Rebuild();return;}if(architecture!=null&&architectureMode=="overview"&&architectureModule.Length==0&&moduleColumns!=(viewport.ActualWidth>=1100?2:1)){architectureHome=true;Rebuild();}};
   viewport.MouseMove+=delegate(object sender,MouseEventArgs e){if(!panning)return;var p=e.GetPosition(viewport);viewport.ScrollToHorizontalOffset(panX-p.X+panStart.X);viewport.ScrollToVerticalOffset(panY-p.Y+panStart.Y);};
   viewport.MouseLeftButtonUp+=delegate{panning=false;viewport.ReleaseMouseCapture();};viewport.LostMouseCapture+=delegate{panning=false;};
   GraphCopyText.Enable(Root);
   archive.Changed+=Refresh;archive.ReadingView=CaptureReadingView;Refresh();
  }
  bool GraphBackground(DependencyObject source){for(var current=source;current!=null;current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current)){if(current==viewport)return true;if(current is TextBlock||current is System.Windows.Controls.Primitives.ButtonBase||current is System.Windows.Controls.Primitives.ScrollBar)return false;}return false;}
  void ArrangeBody(bool value){if(IsFullscreen)return;bool hidden=architecture!=null&&architectureMode!="relations"&&selected.Length==0;detailFrame.Visibility=hidden?Visibility.Collapsed:Visibility.Visible;wide=value&&!hidden;body.ColumnDefinitions[1].Width=new GridLength(wide?380:0);Grid.SetColumn(detailFrame,wide?1:0);Grid.SetRow(detailFrame,wide?0:1);detailFrame.Margin=wide?new Thickness(18,0,0,0):new Thickness(0,12,0,0);}
  public void SelectDomain(string id){foreach(DomainGraph g in domains.Items)if(g.Id==id){domains.SelectedItem=g;return;}}
  void Refresh(){
   if(disposed)return;status.Text=archive.Status;manualEdit.IsEnabled=!archive.Busy&&Selected()!=null;addNode.IsEnabled=!archive.Busy&&graph!=null;if(revision==archive.ProfileRevision&&slotId==archive.Slot.Id)return;
   bool changed=slotId!=archive.Slot.Id;slotId=archive.Slot.Id;revision=archive.ProfileRevision;
   string id=changed?"":ActiveDomain;var list=DomainKnowledge.List(archive.Slot);refreshing=true;domains.ItemsSource=list;graph=list.Find(delegate(DomainGraph g){return g.Id==id;})??(list.Count>0?list[0]:null);domains.SelectedItem=graph;archive.CurrentDomainId=ActiveDomain;
   if(changed){selected=graph!=null&&graph.Nodes.Count>0?graph.Nodes[0].Id:"";search.Clear();Zoom(1);}refreshing=false;PrepareArchitecture();Populate();Rebuild();
  }
  DomainNode Selected(){return graph==null?null:graph.Nodes.Find(delegate(DomainNode n){return n.Id==selected;})??SingleModuleNode(SelectedModule());}
  void Rebuild(){
   if(disposed||refreshing)return;
   RequestFullscreenPosition();
   if(graph==null){generation++;scene=null;IsLayoutReady=true;heading.Text="知识图谱";heading.ToolTip=null;canvas.Children.Clear();canvas.Width=640;canvas.Height=420;var empty=Text("还没有领域图谱。点击右上方「＋ 新领域」开始搭建。",16,"#485A80");empty.Width=520;Canvas.SetLeft(empty,32);Canvas.SetTop(empty,50);canvas.Children.Add(empty);return;}
   heading.Text=graph.Title;heading.ToolTip=graph.Description;if(TryRebuildArchitecture())return;
   var view=new KnowledgeGraphView();var ids=new HashSet<string>();var adjacent=new HashSet<string>{selected};
   foreach(var e in graph.Edges)if(e.From==selected||e.To==selected){adjacent.Add(e.From);adjacent.Add(e.To);}
   string query=search.Text.Trim();foreach(var n in graph.Nodes){if(query.Length>0&&(n.Title+" "+n.Summary+" "+n.Approach+" "+n.Code).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
    view.MatchingCount++;if(view.Nodes.Count>=80)continue;ids.Add(n.Id);view.Nodes.Add(new ArchiveItem{Id=n.Id,Title=n.Title,Domain=graph.Title,Note=n.Summary});}
   if(!ids.Contains(selected)){selected=view.Nodes.Count>0?view.Nodes[0].Id:"";Populate();}
   int i=0;foreach(var e in graph.Edges)if(ids.Contains(e.From)&&ids.Contains(e.To))view.Links.Add(new KnowledgeLink{Id="edge-"+i++,From=e.From,To=e.To,Kind=e.Label,Evidence=e.Evidence,Confirmed=true});
   int request=++generation;IsLayoutReady=false;scene=null;canvas.Children.Clear();canvas.Width=640;canvas.Height=420;
   var message=Text(view.Nodes.Count==0?"这个领域暂无匹配节点。可以清空搜索，或新增节点。":"正在连接领域知识…",16,"#485A80");message.Width=520;Canvas.SetLeft(message,32);Canvas.SetTop(message,50);canvas.Children.Add(message);
   if(view.Nodes.Count==0){IsLayoutReady=true;return;}
   ThreadPool.QueueUserWorkItem(delegate{KnowledgeGraphScene calculated=null;string error="";try{calculated=KnowledgeGraphLayout.Calculate(view);}catch(Exception ex){error=ex.Message;}
    if(canvas.Dispatcher.HasShutdownStarted)return;canvas.Dispatcher.BeginInvoke(new Action(delegate{if(disposed||request!=generation)return;IsLayoutReady=true;if(calculated==null){status.Text="布局失败："+error;return;}scene=calculated;Draw();canvas.UpdateLayout();CenterSelected();}));});
  }
  void Draw(){
   RequestFullscreenPosition();if(scene==null)return;if(DrawArchitecture())return;canvas.Children.Clear();canvas.Width=scene.Width;canvas.Height=scene.Height;
   // Subtle coordinate grid evokes a pixel terminal without reducing text contrast.

   var adjacent=new HashSet<string>{selected};foreach(var e in scene.Edges)if(e.Link.From==selected||e.Link.To==selected){adjacent.Add(e.Link.From);adjacent.Add(e.Link.To);}
   foreach(var e in scene.Edges){bool active=e.Link.From==selected||e.Link.To==selected;var stroke=Theme.Brush(active?"#008B83":"#8F9CBA");double opacity=selected.Length==0||active?1:.28;
    var line=new Polyline{Points=new PointCollection(e.Points),Stroke=stroke,StrokeThickness=active?3:1.5,Opacity=opacity,ToolTip=e.Link.Kind+"\n"+e.Link.Evidence};canvas.Children.Add(line);
    if(e.Points.Count>1){var end=e.Points[e.Points.Count-1];Vector v=end-e.Points[e.Points.Count-2];if(v.Length>0){v.Normalize();var side=new Vector(-v.Y,v.X);canvas.Children.Add(new Polygon{Fill=stroke,Opacity=opacity,Points=new PointCollection(new[]{end,end-v*12+side*5,end-v*12-side*5}),IsHitTestVisible=false});}}
    var label=new Border{Background=Theme.Brush("#F8FAFF"),Padding=new Thickness(5,2,5,2),Opacity=opacity,Child=Text(e.Link.Kind,12,active?"#007F79":"#687794"),ToolTip=e.Link.Evidence};Canvas.SetLeft(label,e.Label.X-50);Canvas.SetTop(label,e.Label.Y-12);canvas.Children.Add(label);
   }
   foreach(var p in scene.Nodes){var n=graph.Nodes.Find(delegate(DomainNode item){return item.Id==p.Item.Id;});if(n==null)continue;string id=n.Id;
    var content=new Grid();content.RowDefinitions.Add(new RowDefinition{Height=new GridLength(5)});content.RowDefinitions.Add(new RowDefinition());
    content.Children.Add(new Border{Background=Accent(n.Tier)});
    var box=new StackPanel{Margin=new Thickness(15,10,15,8)};
    box.Children.Add(Text((n.Tier=="CORE"?"✦ CORE":n.Tier+" / ARCHIVE")+"     "+(id==selected?"● 已选中":"◇"),11,Color(n.Tier)));
    var title=Text(n.Title,24,"#28324A");title.FontWeight=FontWeights.SemiBold;title.MaxHeight=52;title.TextTrimming=TextTrimming.CharacterEllipsis;box.Children.Add(title);
    var subtitle=Text(n.Status.StartsWith("历史")?n.Status:n.Summary,12,"#52617F");subtitle.MaxHeight=32;subtitle.TextTrimming=TextTrimming.CharacterEllipsis;box.Children.Add(subtitle);Grid.SetRow(box,1);content.Children.Add(box);
    var button=Button("",delegate{SelectNode(id);});button.Content=content;button.Width=KnowledgeGraphLayout.NodeWidth;button.Height=KnowledgeGraphLayout.NodeHeight;button.Padding=new Thickness(0);button.Margin=new Thickness(0);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Stretch;
    button.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.ClickCount!=2)return;e.Handled=true;EditNode(n);};
    button.Background=Theme.Brush(id==selected?"#E3ECFF":"#FFFFFF");button.BorderBrush=id==selected?Accent(n.Tier):Theme.Brush("#8996B5");button.BorderThickness=new Thickness(id==selected?2:1);button.Opacity=selected.Length==0||adjacent.Contains(id)?1:.82;button.ToolTip=n.Title+"\n"+DomainKnowledge.TierName(n.Tier)+"\n"+n.Summary;
    Canvas.SetLeft(button,p.X);Canvas.SetTop(button,p.Y);canvas.Children.Add(button);
   }
  }
  public void SelectNode(string id){if(architecture!=null&&architectureMode=="overview"){var owner=RepositoryArchitecture.Parent(architecture,id);if(SingleModuleNode(owner)!=null)id=ModuleKey(owner.Id);}RememberReflection();if(selected!=id)manualDetailsPosition=null;selected=id;fullDetailsVisible=true;
   var selectedModule=SelectedModule();if(architecture!=null&&architectureMode=="overview"&&selectedModule!=null&&architectureModule!=(selectedModule.ParentId??"")){architectureModule=selectedModule.ParentId??"";search.Clear();UpdateArchitectureBar();Populate();Rebuild();CenterSelected();return;}
   if(architecture!=null&&architectureMode=="overview"&&SelectedModule()==null){var parent=RepositoryArchitecture.Parent(architecture,id);if(parent!=null&&architectureModule!=parent.Id){architectureModule=parent.Id;search.Clear();UpdateArchitectureBar();Populate();Rebuild();CenterSelected();return;}}
   Populate();Draw();RequestFullscreenPosition();}
  void Section(string label,string value){inspector.Children.Add(Text(label,12,"#49788F"));inspector.Children.Add(Text(string.IsNullOrWhiteSpace(value)?"尚未补充":value,15,"#33405B"));}
  void Populate(){
   UpdateStudyScope();RememberReflection();ArrangeBody(((FrameworkElement)Root).ActualWidth>=1100);inspector.Children.Clear();if(PopulateArchitecture())return;var node=Selected();manualEdit.IsEnabled=node!=null&&!archive.Busy;addNode.IsEnabled=graph!=null&&!archive.Busy;inspector.Children.Add(Text("NODE / 节点档案",11,"#49788F"));
   if(graph==null){inspector.Children.Add(Text("从一个领域开始",24,"#28324A"));inspector.Children.Add(Text("创建领域后，可以自行添加节点，或让曳尔沐与 DSH 根据源码整理内容。",15,"#53617A"));return;}
   if(node==null){if(architecture!=null&&architectureMode!="relations"){inspector.Children.Add(Text("从整体到源码",24,"#28324A"));inspector.Children.Add(Text("点击模块了解职责与跨模块关系，选择「展开模块」进入源码节点。",15,"#53617A"));inspector.Children.Add(Text(architecture.Scope,12,"#687794"));return;}inspector.Children.Add(Text("点亮一个知识节点",24,"#28324A"));inspector.Children.Add(Text("点击节点查看介绍、实现思路、代码与来源。箭头从起点读向终点；选中后，高亮与它直接相关的知识。",15,"#53617A"));inspector.Children.Add(Button("新增节点",delegate{EditNode(null);}));return;}
   inspector.Children.Add(Text(DomainKnowledge.TierName(node.Tier),13,Color(node.Tier)));inspector.Children.Add(Text(node.Title,24,"#28324A"));
   ArchitectureNodeContext(node);NodeDetails(node);
  }

  void NewDomain(){var graph=DomainGraphEditor.NewDomain(LearningOwner);if(graph!=null&&archive.SaveDomainGraph(graph))SelectDomain(graph.Id);}
  static Button EditAction(string label,Action action){var b=Button(label,action);b.Background=Brushes.Transparent;b.BorderThickness=new Thickness(0);b.Foreground=Theme.Brush("#5368B2");b.FontFamily=Theme.ReadingFont;b.FontSize=14;b.Padding=new Thickness(4,7,8,7);b.Margin=new Thickness(0,0,8,0);return b;}
  void EditNode(DomainNode node){if(archive.Busy){status.Text="请等待当前访谈完成。";return;}var copy=DomainKnowledge.Clone(graph);string editingSlot=archive.Slot.Id,expected=GraphEditing.Revision(copy);string id=DomainGraphEditor.Edit(LearningOwner,copy,node==null?null:node.Id,delegate(DomainGraph changed){var current=DomainKnowledge.List(archive.Slot).Find(delegate(DomainGraph g){return g.Id==copy.Id;});if(archive.Slot.Id!=editingSlot||current==null||GraphEditing.Revision(current)!=expected)return "图谱已被其他操作更新。请复制当前补充内容，重新打开节点后再保存。";return archive.SaveDomainGraph(changed)?"":archive.Status;});if(id!=null){search.Clear();SelectNode(id);Rebuild();}}
  void Zoom(double value){double x=(viewport.HorizontalOffset+viewport.ViewportWidth/2)/scale.ScaleX,y=(viewport.VerticalOffset+viewport.ViewportHeight/2)/scale.ScaleY;value=Math.Max(.15,Math.Min(3,value));scale.ScaleX=scale.ScaleY=value;canvas.UpdateLayout();viewport.ScrollToHorizontalOffset(Math.Max(0,x*value-viewport.ViewportWidth/2));viewport.ScrollToVerticalOffset(Math.Max(0,y*value-viewport.ViewportHeight/2));}
  void CenterSelected(){if(scene==null||scene.Nodes.Count==0)return;var n=scene.Nodes.Find(delegate(GraphNodePlacement p){return p.Item.Id==selected;})??scene.Nodes[0];viewport.ScrollToHorizontalOffset(Math.Max(0,(n.X+140)*scale.ScaleX-viewport.ViewportWidth/2));viewport.ScrollToVerticalOffset(Math.Max(0,(n.Y+71)*scale.ScaleY-viewport.ViewportHeight/2));}
  public void Dispose(){DisposeStudyTools();ExitFullscreen();disposed=true;generation++;searchTimer.Stop();archive.Changed-=Refresh;if(archive.ReadingView==(Func<GraphReadingView>)CaptureReadingView)archive.ReadingView=null;}
 }
}






