using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel {
  readonly StackPanel architectureBar=new StackPanel{Margin=new Thickness(0,0,0,10)};
  RepositoryMap architecture;string architectureDomain="",architectureMode="relations",architectureModule="";int journeyIndex;
  bool architectureHome;int moduleColumns=2,moduleLaneWidth=640;
  readonly Dictionary<string,Point> modulePositions=new Dictionary<string,Point>();
  public string ArchitectureView {get{return architectureMode;}}
  public string ArchitectureModule {get{return architectureModule;}}
  public int JourneyIndex {get{return journeyIndex;}}
  string ModuleKey(string id){return "@architecture/"+id;}
  RepositoryModule SelectedModule(){return architecture==null?null:architecture.Modules.Find(delegate(RepositoryModule m){return ModuleKey(m.Id)==selected;});}
  DomainNode SingleModuleNode(RepositoryModule module){return graph==null||module==null||module.Nodes.Count!=1||RepositoryArchitecture.Children(architecture,module.Id).Count!=0?null:graph.Nodes.Find(n=>n.Id==module.Nodes[0]);}
  void PrepareArchitecture(){
   string id=graph==null?"":graph.Id;bool changed=id!=architectureDomain;architectureDomain=id;architecture=RepositoryArchitecture.Build(graph);
   if(changed){architectureMode=architecture==null?"relations":"overview";architectureModule="";journeyIndex=0;architectureHome=true;if(architecture!=null)selected="";}
   if(architecture==null)architectureMode="relations";
   if(architecture!=null&&!architecture.Modules.Exists(delegate(RepositoryModule m){return m.Id==architectureModule;}))architectureModule="";
   if(architecture!=null&&journeyIndex>=architecture.Journey.Count)journeyIndex=0;
   if(architecture!=null&&architectureMode=="overview"){var module=SelectedModule();if(module!=null)architectureModule=module.ParentId??"";else if(Selected()!=null){var owner=RepositoryArchitecture.Parent(architecture,selected);if(owner!=null)architectureModule=owner.Id;}}
   RefreshGraphEditResults();UpdateArchitectureBar();
  }
  public void ShowArchitecture(string mode){
   if(architecture==null)return;if(mode!="overview"&&mode!="journey"&&mode!="relations")return;
   if(mode=="journey"&&architecture.Journey.Count==0)return;
   RememberReflection();architectureMode=mode;architectureModule="";selected="";search.Clear();fullDetailsVisible=false;
   if(mode=="journey")selected=architecture.Journey[journeyIndex].NodeId;
   architectureHome=true;UpdateArchitectureBar();Populate();Rebuild();
  }
  public void OpenArchitectureModule(string id){
   if(architecture==null||!architecture.Modules.Exists(delegate(RepositoryModule m){return m.Id==id;}))return;
   var module=architecture.Modules.Find(m=>m.Id==id);if(SingleModuleNode(module)!=null){if(architectureMode!="overview")ShowArchitecture("overview");SelectNode(ModuleKey(id));return;}
   RememberReflection();architectureMode="overview";architectureModule=id;selected="";search.Clear();fullDetailsVisible=false;architectureHome=true;UpdateArchitectureBar();Populate();Rebuild();
  }
  public void MoveJourney(int delta){
   if(architecture==null||architecture.Journey.Count==0)return;RememberReflection();journeyIndex=Math.Max(0,Math.Min(architecture.Journey.Count-1,journeyIndex+delta));selected=architecture.Journey[journeyIndex].NodeId;fullDetailsVisible=true;UpdateArchitectureBar();Populate();Rebuild();CenterSelected();
  }
  void UpdateArchitectureBar(){
   architectureBar.Children.Clear();architectureBar.Visibility=architecture==null?Visibility.Collapsed:Visibility.Visible;if(architecture==null)return;
   var modes=new WrapPanel();foreach(var name in new[]{"overview","journey","relations"}){string mode=name;var b=EditAction(name=="overview"?"整体架构":name=="journey"?"请求旅程":"全部关联",delegate{ShowArchitecture(mode);});b.FontWeight=architectureMode==name?FontWeights.Bold:FontWeights.Normal;b.Background=architectureMode==name?Theme.Brush("#E3ECFF"):Brushes.Transparent;b.IsEnabled=name!="journey"||architecture.Journey.Count>0;if(!b.IsEnabled)b.ToolTip="当前专题尚无经过源码核对的请求路线";modes.Children.Add(b);}architectureBar.Children.Add(modes);
   var navigation=new WrapPanel();navigation.Children.Add(Link("整体架构",delegate{ShowArchitecture("overview");}));
   var parent=architecture.Modules.Find(delegate(RepositoryModule m){return m.Id==architectureModule;});
   if(parent!=null){
    navigation.Children.Add(Text("  ›  "+parent.Repository+"  ›  ",14,"#52617F"));
    foreach(var part in RepositoryArchitecture.Path(architecture,parent.Id)){string id=part.Id;var crumb=Link(part.Title,delegate{OpenArchitectureModule(id);});crumb.MaxWidth=220;crumb.TextWrapping=TextWrapping.NoWrap;crumb.TextTrimming=TextTrimming.CharacterEllipsis;crumb.ToolTip=part.Title;navigation.Children.Add(crumb);navigation.Children.Add(Text(" › ",14,"#52617F"));}
    navigation.Children.Add(EditAction(string.IsNullOrEmpty(parent.ParentId)?"返回总览":"返回上级",delegate{if(string.IsNullOrEmpty(parent.ParentId))ShowArchitecture("overview");else OpenArchitectureModule(parent.ParentId);}));
   }
   else if(architectureMode=="journey")navigation.Children.Add(Text("  ›  普通生成请求 · "+(journeyIndex+1)+" / "+architecture.Journey.Count,14,"#52617F"));
   else if(architectureMode=="relations")navigation.Children.Add(Text("  ›  全部源码关联",14,"#52617F"));
   else navigation.Children.Add(Text("  ·  "+architecture.Modules.Count+" 个模块 / "+graph.Nodes.Count+" 个源码节点",14,"#52617F"));
   architectureBar.Children.Add(new ScrollViewer{Content=navigation,MaxHeight=86,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
   var moduleActions=new WrapPanel();moduleActions.Children.Add(EditAction(parent==null?"请曳尔沐整理模块":"请曳尔沐创建 / 调整子模块",delegate{RequestAiGraphEdit(null,parent);}));architectureBar.Children.Add(moduleActions);
   var scope=Text(architecture.Scope,12,"#687794");scope.Margin=new Thickness(0,0,0,4);scope.TextWrapping=TextWrapping.NoWrap;scope.TextTrimming=TextTrimming.CharacterEllipsis;scope.ToolTip=architecture.Scope;architectureBar.Children.Add(scope);
   if(architectureMode=="journey"){
    var step=architecture.Journey[journeyIndex];var row=new WrapPanel();var prev=EditAction("← 上一步",delegate{MoveJourney(-1);});prev.IsEnabled=journeyIndex>0;row.Children.Add(prev);var next=EditAction("下一步 →",delegate{MoveJourney(1);});next.IsEnabled=journeyIndex<architecture.Journey.Count-1;row.Children.Add(next);row.Children.Add(Text(step.Title+"  ·  "+step.Boundary,14,"#247A7B"));architectureBar.Children.Add(row);
   }
  }
  bool TryRebuildArchitecture(){
   if(architecture==null||architectureMode=="relations")return false;
   generation++;flowColumns=0;IsLayoutReady=true;scene=new KnowledgeGraphScene();modulePositions.Clear();string query=search.Text.Trim();
   if(architectureMode=="journey"){
    // Stable spatial layout; repeated visits to EngineCore are represented by separate step cards.
    scene.Width=980;scene.Height=80+Math.Ceiling(architecture.Journey.Count/3.0)*190;
    for(int i=0;i<architecture.Journey.Count;i++){var step=architecture.Journey[i];string key="@step/"+i;int column=(i/3)%2==0?i%3:2-i%3;Point p=new Point(30+column*320,64+(i/3)*190);modulePositions[key]=p;if(i==journeyIndex)scene.Nodes.Add(Placement(step.NodeId,p.X,p.Y));}
   }else if(architectureModule.Length==0){
    var repositories=new List<string>();foreach(var m in RepositoryArchitecture.Children(architecture,""))if(!repositories.Contains(m.Repository))repositories.Add(m.Repository);
    moduleColumns=viewport.ActualWidth>=1100?2:1;moduleLaneWidth=40+moduleColumns*300;
    scene.Width=40+repositories.Count*moduleLaneWidth;scene.Height=450;
    for(int r=0;r<repositories.Count;r++){var modules=architecture.Modules.FindAll(delegate(RepositoryModule m){return string.IsNullOrEmpty(m.ParentId)&&m.Repository==repositories[r];});scene.Height=Math.Max(scene.Height,110+Math.Ceiling(modules.Count/(double)moduleColumns)*180);
     for(int i=0;i<modules.Count;i++){var m=modules[i];var p=new Point(36+r*moduleLaneWidth+(i%moduleColumns)*300,70+(i/moduleColumns)*180);modulePositions[m.Id]=p;scene.Nodes.Add(Placement(ModuleKey(m.Id),p.X,p.Y));}}
   }else{
    var module=architecture.Modules.Find(m=>m.Id==architectureModule);scene.Width=660;int i=0;int columns=viewport.ActualWidth>=980?3:viewport.ActualWidth>=650?2:1;
    foreach(var child in RepositoryArchitecture.Children(architecture,module.Id)){scene.Nodes.Add(Placement(ModuleKey(child.Id),30+(i%columns)*310,70+(i/columns)*190));i++;}
    foreach(var id in module.Nodes){var n=graph.Nodes.Find(x=>x.Id==id);if(!Matches(n,query))continue;scene.Nodes.Add(Placement(id,30+(i%columns)*310,70+(i/columns)*190));i++;}
    scene.Width=Math.Max(350,columns*310+30);scene.Height=Math.Max(350,90+Math.Ceiling(i/(double)columns)*190);
    var view=new KnowledgeGraphView();var ids=new HashSet<string>();
    foreach(var p in scene.Nodes){ids.Add(p.Item.Id);var n=graph.Nodes.Find(x=>x.Id==p.Item.Id);var child=architecture.Modules.Find(m=>ModuleKey(m.Id)==p.Item.Id);view.Nodes.Add(new ArchiveItem{Id=p.Item.Id,Title=n==null?child.Title:n.Title});}
    var seen=new HashSet<string>();foreach(var e in graph.Edges){string from=RepositoryArchitecture.VisibleItem(architecture,module.Id,e.From),to=RepositoryArchitecture.VisibleItem(architecture,module.Id,e.To);if(from==to||!ids.Contains(from)||!ids.Contains(to)||!seen.Add(from+"/"+to+"/"+e.Label))continue;view.Links.Add(new KnowledgeLink{From=from,To=to,Kind=e.Label,Evidence=e.From+" → "+e.To+"\n"+e.Evidence});}
    if(view.Links.Count>0&&!TryFoldModuleFlow(view)){scene=KnowledgeGraphLayout.Calculate(view);foreach(var p in scene.Nodes)p.Y+=70;foreach(var e in scene.Edges){e.Label=new Point(e.Label.X,e.Label.Y+70);for(int j=0;j<e.Points.Count;j++)e.Points[j]=new Point(e.Points[j].X,e.Points[j].Y+70);}scene.Height+=70;}
   }
   Draw();RequestFullscreenPosition();if(architectureHome){architectureHome=false;int request=generation;canvas.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(delegate{if(disposed||request!=generation)return;canvas.UpdateLayout();viewport.ScrollToHorizontalOffset(0);viewport.ScrollToVerticalOffset(0);}));}return true;
  }
  static GraphNodePlacement Placement(string id,double x,double y){return new GraphNodePlacement{Item=new ArchiveItem{Id=id},X=x,Y=y};}
  static bool Matches(DomainNode n,string query){return n!=null&&(query.Length==0||(n.Title+" "+n.Summary+" "+n.Approach+" "+n.Code).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);}
  bool DrawArchitecture(){
   if(architecture==null||architectureMode=="relations")return false;
   canvas.Children.Clear();canvas.Width=scene.Width;canvas.Height=scene.Height;
   if(architectureMode=="journey"){
    CanvasText("普通生成请求 · 逻辑流程示意 / 非运行追踪 · 配置分支见源码",26,20,14,"#52617F");
    for(int i=0;i<architecture.Journey.Count;i++){int index=i;var step=architecture.Journey[i];Point p=modulePositions["@step/"+i];
     if(i>0){var prev=modulePositions["@step/"+(i-1)];bool right=p.X>prev.X;Arrow(i%3==0?new Point(prev.X+140,prev.Y+142):new Point(prev.X+(right?280:0),prev.Y+71),i%3==0?new Point(p.X+140,p.Y):new Point(p.X+(right?0:280),p.Y+71),false,"流程下一步",.55);}
     ArchitectureCard(step.NodeId,(i+1).ToString("00")+" / "+step.Boundary,step.Title,step.Input+" → "+step.Output,p,i==journeyIndex,delegate{RememberReflection();journeyIndex=index;selected=step.NodeId;fullDetailsVisible=true;UpdateArchitectureBar();Populate();Rebuild();CenterSelected();},null,1);
    }return true;
   }
   if(architectureModule.Length>0){
    var m=architecture.Modules.Find(x=>x.Id==architectureModule);var caption=Text(m.Title+" · "+RepositoryArchitecture.Children(architecture,m.Id).Count+" 个子模块 / "+m.Nodes.Count+" 个直属节点",16,"#247A7B");caption.Width=Math.Max(260,scene.Width-60);caption.MaxHeight=48;caption.TextTrimming=TextTrimming.CharacterEllipsis;Canvas.SetLeft(caption,30);Canvas.SetTop(caption,14);canvas.Children.Add(caption);
    DrawModuleRelations();
    foreach(var p in scene.Nodes){string id=p.Item.Id;var child=architecture.Modules.Find(x=>ModuleKey(x.Id)==id);if(child!=null){DrawModuleCard(child,new Point(p.X,p.Y));continue;}var n=graph.Nodes.Find(x=>x.Id==id);ArchitectureCard(id,m.Repository+" / "+DomainKnowledge.TierName(n.Tier),n.Title,n.Summary,new Point(p.X,p.Y),selected==id,delegate{SelectNode(id);},null,1);}
    if(scene.Nodes.Count==0)CanvasText(search.Text.Trim().Length>0?"没有匹配节点。清空搜索可恢复模块内容。":"模块暂时为空。可在上方委托曳尔沐添加子模块或节点。",30,90,16,"#52617F");return true;
   }
   var repos=new List<string>();foreach(var m in RepositoryArchitecture.Children(architecture,""))if(!repos.Contains(m.Repository))repos.Add(m.Repository);
   for(int i=0;i<repos.Count;i++){var bg=new Border{Width=moduleLaneWidth-20,Height=scene.Height-22,Background=Theme.Brush(i==0?"#EDF6F6":"#F1EFF9"),BorderBrush=Theme.Brush("#CED8E6"),BorderThickness=new Thickness(1),IsHitTestVisible=false};Canvas.SetLeft(bg,20+i*moduleLaneWidth);Canvas.SetTop(bg,10);canvas.Children.Add(bg);CanvasText(repos[i],36+i*moduleLaneWidth,25,20,i==0?"#247A7B":"#70609D");}
   var selectedModule=SelectedModule();var drawn=new HashSet<string>();foreach(var e in graph.Edges){string fromKey=RepositoryArchitecture.VisibleItem(architecture,"",e.From),toKey=RepositoryArchitecture.VisibleItem(architecture,"",e.To);var from=architecture.Modules.Find(m=>ModuleKey(m.Id)==fromKey);var to=architecture.Modules.Find(m=>ModuleKey(m.Id)==toKey);if(from==null||to==null||from==to)continue;
    bool primary=(e.From=="entry-async-llm"&&e.To=="engine-core-step")||(e.From=="engine-core-step"&&e.To=="scheduler-schedule")||(e.From=="executor-boundary"&&e.To=="worker-gpu")||(e.From=="platform-npu"&&e.To=="platform-interface")||(e.From=="platform-npu"&&e.To=="worker-npu");
    if((selectedModule==null?!primary:from!=selectedModule&&to!=selectedModule)||!drawn.Add(from.Id+"/"+to.Id))continue;
    Point a=modulePositions[from.Id],b=modulePositions[to.Id];bool right=b.X>a.X;Point start=new Point(a.X+(right?280:0),a.Y+71),end=new Point(b.X+(right?0:280),b.Y+71);
    if(Math.Abs(a.X-b.X)<1){bool down=b.Y>a.Y;start=new Point(a.X+140,a.Y+(down?142:0));end=new Point(b.X+140,b.Y+(down?0:142));}Arrow(start,end,from.Repository!=to.Repository,e.Label,.7);
   }
   foreach(var m in RepositoryArchitecture.Children(architecture,""))DrawModuleCard(m,modulePositions[m.Id]);return true;
  }
  void DrawModuleCard(RepositoryModule module,Point position){
   var members=RepositoryArchitecture.Members(architecture,module.Id);string query=search.Text.Trim();int matches=members.FindAll(id=>Matches(graph.Nodes.Find(n=>n.Id==id),query)).Count;
   bool matched=query.Length==0||matches>0||architecture.Modules.Exists(m=>RepositoryArchitecture.Path(architecture,m.Id).Exists(p=>p.Id==module.Id)&&(m.Title+" "+m.Summary).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);
   int children=RepositoryArchitecture.Children(architecture,module.Id).Count;
   bool direct=SingleModuleNode(module)!=null;string badge=direct?"源码节点 · 点击阅读":children+" 个子模块 / "+members.Count+" 个源码节点";
   ArchitectureCard(ModuleKey(module.Id),badge+(query.Length>0?" · 命中 "+matches:""),module.Title,module.Summary,position,selected==ModuleKey(module.Id),delegate{SelectNode(ModuleKey(module.Id));},direct?null:(Action)delegate{OpenArchitectureModule(module.Id);},matched?1:.35);
  }
  void DrawModuleRelations(){
   foreach(var e in scene.Edges){bool active=selected.Length==0||e.Link.From==selected||e.Link.To==selected;var brush=Theme.Brush(active?"#247A7B":"#8F9CBA");var line=new System.Windows.Shapes.Polyline{Points=new PointCollection(e.Points),Stroke=brush,StrokeThickness=active?2:1,Opacity=active?1:.35,ToolTip=e.Link.Kind+"\n"+e.Link.Evidence};canvas.Children.Add(line);
    if(e.Points.Count>1){var end=e.Points[e.Points.Count-1];Vector v=end-e.Points[e.Points.Count-2];if(v.Length>0){v.Normalize();var side=new Vector(-v.Y,v.X);canvas.Children.Add(new System.Windows.Shapes.Polygon{Fill=brush,Points=new PointCollection(new[]{end,end-v*10+side*4,end-v*10-side*4}),IsHitTestVisible=false});}}
    var text=Text(e.Link.Kind,12,"#247A7B");text.Width=180;text.MaxHeight=44;text.TextTrimming=TextTrimming.CharacterEllipsis;text.Margin=new Thickness(0);var label=new Border{Child=text,Background=Theme.Brush("#F8FAFF"),ToolTip=e.Link.Kind+"\n"+e.Link.Evidence,Tag="module-relation"};Canvas.SetLeft(label,e.Label.X-90);Canvas.SetTop(label,e.Label.Y-16);canvas.Children.Add(label);
   }
  }
  void CanvasText(string value,double x,double y,double size,string color){var text=Text(value,size,color);Canvas.SetLeft(text,x);Canvas.SetTop(text,y);canvas.Children.Add(text);}
  string ArchitectureTier(string id){
   var node=graph.Nodes.Find(delegate(DomainNode n){return n.Id==id;});if(node!=null)return node.Tier;
   var module=architecture.Modules.Find(delegate(RepositoryModule m){return ModuleKey(m.Id)==id;});
   if(module!=null){var members=new HashSet<string>(RepositoryArchitecture.Members(architecture,module.Id));foreach(var tier in DomainKnowledge.Tiers)if(graph.Nodes.Exists(n=>members.Contains(n.Id)&&n.Tier==tier))return tier;}
   return "A";
  }
  void ArchitectureCard(string id,string badge,string title,string summary,Point position,bool active,Action select,Action expand,double opacity){
   var accent=Accent(ArchitectureTier(id));var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(5)});
   var box=new StackPanel{Margin=new Thickness(13,9,13,6)};var tag=Text(badge,11,"#687794");tag.TextWrapping=TextWrapping.NoWrap;tag.TextTrimming=TextTrimming.CharacterEllipsis;tag.Margin=new Thickness(0,0,0,5);box.Children.Add(tag);
   var heading=Text(title,19,"#28324A");heading.FontWeight=FontWeights.SemiBold;heading.MaxHeight=48;heading.TextTrimming=TextTrimming.CharacterEllipsis;heading.Margin=new Thickness(0,0,0,5);box.Children.Add(heading);
   var body=Text(summary,13,"#52617F");body.MaxHeight=38;body.TextTrimming=TextTrimming.CharacterEllipsis;body.Margin=new Thickness(0);box.Children.Add(body);
   var pick=Button("",select);pick.Content=box;pick.Padding=new Thickness(0);pick.Margin=new Thickness(0);pick.BorderThickness=new Thickness(0);pick.Background=Brushes.Transparent;pick.HorizontalContentAlignment=HorizontalAlignment.Stretch;pick.VerticalContentAlignment=VerticalAlignment.Stretch;pick.ToolTip=title+"\n"+summary;grid.Children.Add(pick);
   if(expand!=null){var open=EditAction("展开模块 →",expand);open.Margin=new Thickness(12,0,0,0);open.Padding=new Thickness(0,2,8,5);open.FontSize=12;open.HorizontalAlignment=HorizontalAlignment.Left;Grid.SetRow(open,1);grid.Children.Add(open);}
   var stripe=new Border{Background=accent,IsHitTestVisible=false};Grid.SetRow(stripe,2);grid.Children.Add(stripe);
   var border=new Border{Width=280,Height=142,Background=Theme.Brush(active?"#E3ECFF":"#FFFFFF"),BorderBrush=active?accent:Theme.Brush("#8996B5"),BorderThickness=new Thickness(active?2:1),Child=grid,Opacity=opacity,Tag=id};Canvas.SetLeft(border,position.X);Canvas.SetTop(border,position.Y);canvas.Children.Add(border);
  }
  void Arrow(Point a,Point b,bool dashed,string hint,double opacity){
   var path=new System.Windows.Shapes.Path{Stroke=Theme.Brush("#70869E"),StrokeThickness=2,Opacity=opacity,ToolTip=hint};if(dashed)path.StrokeDashArray=new DoubleCollection(new double[]{4,3});double mid=(a.X+b.X)/2;var figure=new PathFigure{StartPoint=a};figure.Segments.Add(new BezierSegment(new Point(mid,a.Y),new Point(mid,b.Y),b,true));path.Data=new PathGeometry(new[]{figure});canvas.Children.Add(path);Vector direction=Math.Abs(a.X-b.X)<1?new Vector(0,b.Y>=a.Y?1:-1):new Vector(b.X>=a.X?1:-1,0);var side=new Vector(-direction.Y,direction.X);canvas.Children.Add(new Polygon{Fill=Theme.Brush("#70869E"),Opacity=opacity,IsHitTestVisible=false,Points=new PointCollection(new[]{b,b-direction*9+side*5,b-direction*9-side*5})});
  }
  bool PopulateArchitecture(){
   if(architecture==null)return false;
   var m=SelectedModule();if(m==null||SingleModuleNode(m)!=null)return false;manualEdit.IsEnabled=false;inspector.Children.Clear();inspector.Children.Add(Text(m.Repository+"  /  "+RepositoryArchitecture.PathLabel(architecture,m.Id),12,"#247A7B"));inspector.Children.Add(Text(m.Title,24,"#28324A"));inspector.Children.Add(Text(m.Summary,15,"#33405B"));inspector.Children.Add(Link("展开模块 · "+RepositoryArchitecture.Children(architecture,m.Id).Count+" 个子模块 / "+RepositoryArchitecture.Members(architecture,m.Id).Count+" 个源码节点 →",delegate{OpenArchitectureModule(m.Id);}));
   inspector.Children.Add(Link("请曳尔沐新增节点",delegate{RequestAiGraphEdit(null,m);}));
   inspector.Children.Add(Link("请曳尔沐创建 / 调整子模块",delegate{RequestAiGraphEdit(null,m);}));
   var links=new StackPanel();int count=0;foreach(var e in graph.Edges){var members=RepositoryArchitecture.Members(architecture,m.Id);bool from=members.Contains(e.From),to=members.Contains(e.To);if(from==to)continue;string id=from?e.To:e.From;var n=graph.Nodes.Find(delegate(DomainNode x){return x.Id==id;});if(n==null)continue;links.Children.Add(Link((from?"→ ":"← ")+e.Label+" · "+n.Title,delegate{SelectNode(id);},e.Evidence));count++;}
   inspector.Children.Add(new Expander{Header="跨模块关系 · "+count,Content=links,IsExpanded=false});inspector.Children.Add(Text("实线：仓库内关联 · 虚线：跨仓关联。展开关系可查看原始语义与依据；线条不统一代表调用。",12,"#687794"));return true;
  }
  void ArchitectureNodeContext(DomainNode node){
   if(architecture==null)return;var parent=RepositoryArchitecture.Parent(architecture,node.Id);if(parent!=null)inspector.Children.Add(Link(parent.Repository+" › "+RepositoryArchitecture.PathLabel(architecture,parent.Id),delegate{OpenArchitectureModule(parent.Id);}));
   if(architectureMode=="journey"){
    var step=architecture.Journey[journeyIndex];if(step.NodeId==node.Id){inspector.Children.Add(Text(step.Boundary,12,"#247A7B"));inspector.Children.Add(Text(step.Summary,14,"#33405B"));inspector.Children.Add(Text("输入 · "+step.Input+"\n输出 · "+step.Output,13,"#52617F"));}
   }
  }
 }
}
