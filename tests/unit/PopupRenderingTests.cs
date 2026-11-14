using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace ElymsPet {
 static class PopupRenderingTests {
  static int checks,opened;
  static readonly List<Exception> dispatcherErrors=new List<Exception>();
  static void Check(bool pass,string name){if(!pass)throw new Exception("Popup rendering: "+name);checks++;Console.WriteLine("PASS popup: "+name);}
  static void Pump() {
   var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate {frame.Continue=false;}));Dispatcher.PushFrame(frame);
   if(dispatcherErrors.Count>0)throw new Exception("Deferred popup failure",dispatcherErrors[0]);
  }
  static List<T> Visuals<T>(DependencyObject root) where T:DependencyObject {
   var result=new List<T>();if(root is T)result.Add((T)root);
   for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)result.AddRange(Visuals<T>(VisualTreeHelper.GetChild(root,i)));
   return result;
  }
  static ToolTip OpenTip(object content,UIElement target) {
   var tip=new ToolTip{Content=content,PlacementTarget=target,Placement=PlacementMode.Bottom,StaysOpen=true};tip.Opened+=delegate {opened++;};
   try {tip.IsOpen=true;tip.UpdateLayout();Pump();return tip;}catch{try{tip.IsOpen=false;}catch{}throw;}
  }
  static void Close(ToolTip tip){tip.IsOpen=false;Pump();}
  static void Snapshot(FrameworkElement visual,string name) {
   visual.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(visual.ActualWidth)),Math.Max(1,(int)Math.Ceiling(visual.ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
   var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("tests/popup-preview");using(var file=File.Create("tests/popup-preview/"+name+".png"))png.Save(file);
  }
  public static void Run(string root) {
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"popup-isolation");
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){dispatcherErrors.Add(e.Exception);e.Handled=true;};
   MenuTheme.Install(app);
   var button=new Button{Content="Hover target",Width=150,Height=40};var host=new Window{Content=button,Width=500,Height=200,ShowInTaskbar=false,ShowActivated=false};
   PetWindow pet=null;ControlWindow panel=null;ChatBarWindow bar=null;KnowledgeGraphPanel graph=null;
   var report=new StringBuilder();
   try {
    host.Show();host.UpdateLayout();
    // This intentionally opens a real Popup: parsing the style alone misses deferred template failures.
    var tip=OpenTip("关闭",button);
    Check(tip.IsOpen&&tip.ActualWidth>0&&PresentationSource.FromVisual(tip)!=null,"short tooltip creates a real native popup");Close(tip);
    string longText=string.Concat(new string[1]{new string('知',500)});
    tip=OpenTip(longText,button);
    var texts=Visuals<TextBlock>(tip);bool wraps=texts.Exists(delegate(TextBlock x){return x.Text==longText&&x.TextWrapping==TextWrapping.Wrap;});
    Check(wraps&&tip.ActualWidth<=361&&tip.ActualHeight>60,"long Chinese text wraps within 360 pixels");Snapshot(tip,"chinese-wrap");Close(tip);
    tip=OpenTip("https://example.org/"+new string('x',600),button);Check(tip.ActualWidth<=361&&tip.ActualHeight>40,"unbroken URLs wrap without expanding popup width");Close(tip);
    tip=OpenTip("第一行\n第二行\n第三行 · 中文 / emoji 🌱",button);Check(Visuals<TextBlock>(tip).Exists(delegate(TextBlock x){return x.Text.Contains("第二行");})&&tip.ActualHeight>45,"multiline Unicode tooltip preserves content");Snapshot(tip,"multiline");
    tip.Content="运行中更新提示";tip.UpdateLayout();Pump();Check(Visuals<TextBlock>(tip).Exists(delegate(TextBlock x){return x.Text=="运行中更新提示";}),"open tooltip content can change");Close(tip);
    foreach(object empty in new object[]{"",null}){tip=OpenTip(empty,button);Check(tip.ActualWidth<=361,"empty or null content opens safely");Close(tip);}
    var rich=new Border{Child=new TextBlock{Text="富内容提示",Foreground=Brushes.DarkGreen}};tip=OpenTip(rich,button);Check(Visuals<Border>(tip).Contains(rich),"custom visual tooltip content remains supported");Close(tip);
    int before=opened;for(int i=0;i<40;i++){tip=OpenTip("切换提示 "+i,button);Close(tip);}Check(opened-before==40,"40 repeated open/close cycles without deferred exceptions");
    var menu=MenuTheme.Create(true);var submenu=MenuTheme.Item("展开", "",null);submenu.Items.Add(MenuTheme.Item("子菜单项","",null));menu.Items.Add(submenu);menu.PlacementTarget=button;menu.IsOpen=true;Pump();menu.UpdateLayout();submenu.IsSubmenuOpen=true;Pump();Check(menu.IsOpen&&submenu.IsSubmenuOpen,"branded context menu and submenu render");submenu.IsSubmenuOpen=false;menu.IsOpen=false;Pump();
    var edit=MenuTheme.EditMenu(new TextBox{Text="copy"});edit.PlacementTarget=button;edit.IsOpen=true;Pump();Check(edit.ActualWidth>0,"text editing context menu renders");edit.IsOpen=false;Pump();
    var promptType=typeof(PopupDialog).Assembly.GetType("ElymsPet.PromptWindow");var prompt=(Window)Activator.CreateInstance(promptType,new object[]{"弹窗测试，不操作用户资料。","测试",MessageBoxButton.YesNo,MessageBoxImage.Information});
    prompt.ShowActivated=false;prompt.Show();prompt.UpdateLayout();var closeButton=Visuals<Button>(prompt).Find(delegate(Button b){return (string)b.ToolTip=="关闭";});
    Check(closeButton!=null,"confirmation dialog exposes close tooltip");tip=OpenTip(closeButton.ToolTip,closeButton);Close(tip);prompt.Close();Pump();
    pet=new PetWindow(new PetState(),true);RadialMenuTests.Run(pet);panel=new ControlWindow(pet);panel.ShowActivated=false;panel.Show();
    panel.SelectTab(PanelTab.Graph);panel.UpdateLayout();Pump();
    var zoom=panel.ReadingView;Check(!zoom.HandleWheel(120,System.Windows.Input.ModifierKeys.None)&&zoom.Factor==1,"plain wheel leaves reading scale unchanged");
    Check(zoom.HandleWheel(120,System.Windows.Input.ModifierKeys.Control)&&Math.Abs(zoom.Factor-1.1)<.001&&Math.Abs(pet.State.ReadingScale-1.1)<.001,"Ctrl wheel scales entire page and saves preference");
    zoom.SetFactor(1.5);panel.UpdateLayout();Pump();Snapshot(panel,"reading-zoom-150");Check(((FrameworkElement)zoom.Content).LayoutTransform is ScaleTransform&&zoom.ExtentWidth>0,"reading zoom measures finite page at 150 percent");
    panel.SelectTab(PanelTab.Notes);Check(zoom.Factor==1.5,"reading scale survives page changes");
    zoom.SetFactor(20);Check(zoom.Factor==2,"reading scale upper bound");zoom.SetFactor(.1);Check(zoom.Factor==.8,"reading scale lower bound");zoom.SetFactor(1);
    var legacy=GraphEditing.Parse<PetState>("{}");legacy.Normalize();Check(legacy.ReadingScale==1,"legacy settings default to readable 100 percent");
    var samples=new HashSet<string>();int pageCount=0;
    for(int i=0;i<panel.Tabs.Items.Count;i++) {
     panel.Tabs.SelectedIndex=i;panel.UpdateLayout();Pump();CollectTips(panel,samples);pageCount++;
     foreach(var nested in Visuals<TabControl>(panel))if(nested!=panel.Tabs)for(int j=0;j<nested.Items.Count;j++){nested.SelectedIndex=j;panel.UpdateLayout();Pump();CollectTips(panel,samples);}
    }
    Check(pageCount==Enum.GetValues(typeof(PanelTab)).Length,"all registered control-panel pages instantiate with installed theme");
    bar=new ChatBarWindow(pet);bar.ShowBar();bar.UpdateLayout();CollectTips(bar,samples);ChatActivityTests.Run(pet,panel,bar,Snapshot);bar.ShowArchive();bar.UpdateLayout();Pump();CollectTips(bar,samples);
    Check(samples.Count>=15,"collect real tooltip content from panel and floating chat/archive modes");
    foreach(string sample in samples){tip=OpenTip(sample,button);Check(tip.ActualWidth<=361,"real app tooltip fits: "+ChatText.Clip(sample,24));Close(tip);}
    panel.SelectTab(PanelTab.Archive);panel.UpdateLayout();Pump();var interviewTabs=Visuals<TabControl>(panel).Find(delegate(TabControl t){return t!=panel.Tabs;});
    Check(interviewTabs!=null&&((TabItem)interviewTabs.Items[0]).Header.ToString()=="交流新内容"&&((TabItem)interviewTabs.Items[1]).Header.ToString()=="补充已有流程","new discussion and workflow review are independent pages");
    interviewTabs.SelectedIndex=0;panel.UpdateLayout();Pump();var newAnswer=Visuals<TextBox>(interviewTabs).Find(delegate(TextBox t){return t.MaxLength==2000;});newAnswer.Text="新话题草稿";
    interviewTabs.SelectedIndex=1;panel.UpdateLayout();Pump();var reviewAnswer=Visuals<TextBox>(interviewTabs).Find(delegate(TextBox t){return t.MaxLength==2000;});reviewAnswer.Text="流程补充草稿";
    interviewTabs.SelectedIndex=0;panel.UpdateLayout();Pump();Check(newAnswer!=reviewAnswer&&newAnswer.Text=="新话题草稿"&&reviewAnswer.Text=="流程补充草稿","switching interview pages retains separate unsent input");
    var workflow=new ArchiveWorkflow{Title="页面隔离测试",Trigger="开始",Preconditions="条件",Steps="1. 测试",Checks="完成",Exceptions="重试"};pet.Archive.Slot.Workflows.Add(workflow);pet.Archive.StartWorkflowReview(workflow);
    Check(!newAnswer.IsEnabled&&reviewAnswer.IsEnabled,"workflow answers cannot be submitted from new discussion page");pet.Archive.EndWorkflowReview();
    var slot=pet.Archive.Slot;var a=slot.Items[0];var secondNode=slot.Items[1];secondNode.Domain=a.Domain;pet.Archive.SaveKnowledgeLink(a.Id,secondNode.Id,"相关","图谱悬停测试依据，仅使用预览数据");
    pet.Archive.Create("空图谱测试");using(var emptyGraph=new KnowledgeGraphPanel(pet.Archive,delegate{})){host.Content=emptyGraph.Root;host.UpdateLayout();Pump();Check(emptyGraph.IsLayoutReady&&emptyGraph.ActiveDomain==""&&Visuals<TextBlock>(host).Exists(delegate(TextBlock t){return t.Text.Contains("还没有领域图谱");})&&!Visuals<Button>(host).Find(delegate(Button b){return b.Content as string=="＋ 新增节点";}).IsEnabled,"empty graph offers domain creation without resurrecting built-in content");}pet.Archive.Select(slot);
    graph=new KnowledgeGraphPanel(pet.Archive,delegate {});graph.SelectDomain("archive:"+a.Domain);host.Content=graph.Root;host.Width=900;host.Height=900;
    var deadline=DateTime.UtcNow.AddSeconds(15);while(!graph.IsLayoutReady&&DateTime.UtcNow<deadline){Pump();System.Threading.Thread.Sleep(10);}host.UpdateLayout();Pump();
    Check(graph.IsLayoutReady,"graph asynchronous layout completes with themed tooltips");
    var graphTips=new HashSet<string>();CollectTips(host,graphTips);Check(graphTips.ExistsText("图谱悬停测试依据"),"graph edge and node tooltip contents are available");
    foreach(string sample in graphTips){tip=OpenTip(sample,button);Close(tip);}Check(true,"graph node/edge tooltip popups render");
    var graphCanvas=Visuals<Canvas>(host).Find(delegate(Canvas c){return c.LayoutTransform is ScaleTransform;});
    Check(graphCanvas!=null&&((ScaleTransform)graphCanvas.LayoutTransform).ScaleX==1,"graph opens at readable 100 percent instead of shrinking entire layout");
    var graphViewport=Visuals<ScrollViewer>(host).Find(delegate(ScrollViewer v){return v.Content==graphCanvas;});var wheelEvent=new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=UIElement.PreviewMouseWheelEvent};graphViewport.RaiseEvent(wheelEvent);Check(wheelEvent.Handled&&((ScaleTransform)graphCanvas.LayoutTransform).ScaleX>1,"mouse wheel zooms directly and does not scroll parent page");
    double readingZoom=((ScaleTransform)graphCanvas.LayoutTransform).ScaleX;
    pet.Archive.SaveKnowledgeNode("","刷新时保留阅读字号","测试","待核实","了解","测试","");
    deadline=DateTime.UtcNow.AddSeconds(15);while(!graph.IsLayoutReady&&DateTime.UtcNow<deadline){Pump();System.Threading.Thread.Sleep(10);}host.UpdateLayout();Pump();
    Check(graph.IsLayoutReady&&((ScaleTransform)graphCanvas.LayoutTransform).ScaleX==readingZoom,"graph refresh preserves chosen reading zoom");
    var reset=Visuals<Button>(host).Find(delegate(Button b){return b.ToolTip as string=="复原视图";});reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(((ScaleTransform)graphCanvas.LayoutTransform).ScaleX==1&&!Visuals<Button>(host).Exists(delegate(Button b){return b.Content as string=="大窗口查看";}),"single reset restores view without a separate zoom page");
    pet.Archive.SaveDomainGraph(DomainGraphTests.Fixture());graph.SelectDomain("test-domain");graph.SelectNode("mc2");host.Width=1440;host.Height=1000;
    deadline=DateTime.UtcNow.AddSeconds(15);while(!graph.IsLayoutReady&&DateTime.UtcNow<deadline){Pump();System.Threading.Thread.Sleep(10);}host.UpdateLayout();Pump();Snapshot((FrameworkElement)graph.Root,"domain-graph");var tutorial=RepositoryLearningExample.Create();tutorial.Nodes.Find(delegate(DomainNode n){return n.Id=="platform";}).Approach="先调用 `InputProcessor.process_inputs` 准备请求；再检查请求是否能进入队列；使用 `await engine_core.add_request_async` 发送给执行进程。关键结论：这里是跨进程边界。注意：本地文件版本应与来源一致。";pet.Archive.SaveDomainGraph(tutorial);graph.SelectDomain(tutorial.Id);graph.SelectNode("platform");deadline=DateTime.UtcNow.AddSeconds(15);while(!graph.IsLayoutReady&&DateTime.UtcNow<deadline){Pump();System.Threading.Thread.Sleep(10);}host.UpdateLayout();Pump();Snapshot((FrameworkElement)graph.Root,"repository-learning");Check(Visuals<TextBlock>(host).Exists(delegate(TextBlock t){return new System.Windows.Documents.TextRange(t.ContentStart,t.ContentEnd).Text.Contains("跳转代码");})&&!Visuals<Button>(host).Exists(delegate(Button b){return b.Content as string=="曳尔沐讲解";}),"overview has code hyperlink without learning controls");Check(Visuals<Expander>(host).Exists(delegate(Expander e){return (e.Header as string??"").StartsWith("继续阅读")&&!e.IsExpanded;})&&Visuals<TextBlock>(host).Exists(delegate(TextBlock t){return new System.Windows.Documents.TextRange(t.ContentStart,t.ContentEnd).Text=="本地打开";}),"approach uses collapsed reading chunks and exposes local source link");
    var detailTabs=Visuals<TabControl>(host).Find(delegate(TabControl t){return t.Items.Count==4&&((TabItem)t.Items[0]).Header as string=="概览";});Check(detailTabs!=null,"inspector groups overview code learning and resources");detailTabs.SelectedIndex=3;host.UpdateLayout();Pump();Check(Visuals<Expander>(host).Exists(delegate(Expander e){return e.Header as string=="候选资料与筛选原因"&&!e.IsExpanded;}),"unreviewed resources stay collapsed in resources tab");Snapshot((FrameworkElement)graph.Root,"repository-resources");detailTabs.SelectedIndex=2;host.UpdateLayout();Pump();Snapshot((FrameworkElement)graph.Root,"repository-study");detailTabs.SelectedIndex=1;host.UpdateLayout();Pump();Snapshot((FrameworkElement)graph.Root,"repository-code");graph.SelectDomain("test-domain");graph.SelectNode("mc2");host.UpdateLayout();Pump();
    Check(Visuals<TextBox>(host).Exists(delegate(TextBox b){return b.IsReadOnly&&b.Text.Contains("enable_fused_mc2");}),"selected domain node exposes real code in inspector");
    var manualEdit=Visuals<Button>(host).Find(delegate(Button b){return b.Content as string=="编辑节点";});Check(manualEdit!=null&&manualEdit.IsEnabled&&manualEdit.BorderThickness.Left==0,"manual node editing is visible as a lightweight action");
    bool insideScroll=false;for(DependencyObject parent=VisualTreeHelper.GetParent(manualEdit);parent!=null;parent=VisualTreeHelper.GetParent(parent))if(parent is ScrollViewer)insideScroll=true;Check(!insideScroll,"manual edit actions remain outside scrolling node details");
    var right=KnowledgeGraphPanel.PlaceDetails(new Rect(100,100,280,142),new Size(420,600),new Size(1400,900));var left=KnowledgeGraphPanel.PlaceDetails(new Rect(1050,700,280,142),new Size(420,600),new Size(1400,900));Check(right.Left>380&&left.Right<1050&&left.Bottom<=888,"fullscreen callout picks available side and clamps bottom edge");
    graph.SelectDomain(tutorial.Id);graph.SelectNode("platform");deadline=DateTime.UtcNow.AddSeconds(15);while(!graph.IsLayoutReady&&DateTime.UtcNow<deadline){Pump();System.Threading.Thread.Sleep(10);}host.UpdateLayout();Pump();
    var learningTabs=Visuals<TabControl>(host).Find(delegate(TabControl t){return t.Items.Count==4&&((TabItem)t.Items[0]).Header as string=="概览";});learningTabs.SelectedIndex=2;host.UpdateLayout();Pump();var practice=Visuals<Expander>(host).Find(delegate(Expander e){return e.Header as string=="自测 · 用自己的话解释";});practice.IsExpanded=true;host.UpdateLayout();Pump();var reflectionDraft=Visuals<TextBox>(host).Find(delegate(TextBox t){return t.MaxLength==12000;});reflectionDraft.Text="全屏切换保留的未保存复述";
    graph.EnterFullscreen();Pump();Window full=null;foreach(Window w in Application.Current.Windows)if(w.Title=="曳尔沐 · 全屏学习")full=w;Check(full!=null&&graph.IsFullscreen&&full.WindowStyle==WindowStyle.None,"fullscreen is borderless and opens on demand");full.UpdateLayout();Pump();
    Check(Visuals<TextBox>(full).Contains(reflectionDraft),"fullscreen reuses live inspector including unsaved draft");Snapshot(full,"graph-fullscreen-learning");
    var overlay=Visuals<Canvas>(full).Find(delegate(Canvas c){return c.Children.Count==1&&c.Children[0] is Border;});var callout=(Border)overlay.Children[0];Check(callout.Visibility==Visibility.Visible&&Canvas.GetLeft(callout)>=0&&Canvas.GetLeft(callout)+callout.ActualWidth<=overlay.ActualWidth&&Canvas.GetTop(callout)+callout.ActualHeight<=overlay.ActualHeight,"node callout stays within fullscreen viewport");
    Check(Visuals<ReadingZoom>(full)[0].ScrollableWidth<1&&Visuals<ReadingZoom>(full)[0].ScrollableHeight<1,"fullscreen chrome fits screen without outer scrollbars");
    var reader=Visuals<ReadingZoom>(full)[0];reader.SetFactor(1.5);full.UpdateLayout();Pump();Check(callout.Visibility==Visibility.Visible&&Canvas.GetLeft(callout)+callout.ActualWidth<=overlay.ActualWidth,"fullscreen callout remains bounded after 150 percent reading zoom");Snapshot(full,"graph-fullscreen-150");reader.SetFactor(1);full.UpdateLayout();Pump();
    var insideClick=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent};reflectionDraft.RaiseEvent(insideClick);Check(callout.Visibility==Visibility.Visible,"clicking inside fullscreen details keeps reading and editing available");
    var outsideClick=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.PreviewMouseDownEvent};graphViewport.RaiseEvent(outsideClick);Pump();Check(callout.Visibility!=Visibility.Visible&&graph.IsFullscreen,"clicking outside dismisses details without exiting fullscreen");graphViewport.ReleaseMouseCapture();
    var returnToNode=Visuals<Button>(full).Find(delegate(Button b){return b.Content as string=="定位当前节点";});returnToNode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));full.UpdateLayout();Pump();Check(callout.Visibility==Visibility.Visible&&reflectionDraft.Text=="全屏切换保留的未保存复述","dismissed details reopen with unsaved reflection preserved");
    var backgroundDown=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent};graphViewport.RaiseEvent(backgroundDown);Check(backgroundDown.Handled,"fullscreen viewport blank area supports drag panning outside graph bounds");graphViewport.ReleaseMouseCapture();
    var fullWheel=new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=UIElement.PreviewMouseWheelEvent};graphViewport.RaiseEvent(fullWheel);full.UpdateLayout();Pump();Check(fullWheel.Handled,"fullscreen graph retains wheel zoom");
    var dismiss=Visuals<Button>(full).Find(delegate(Button b){return b.ToolTip as string=="收起节点说明";});dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(callout.Visibility!=Visibility.Visible,"callout can be dismissed without exiting fullscreen");
    var locate=Visuals<Button>(full).Find(delegate(Button b){return b.Content as string=="定位当前节点";});locate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));full.UpdateLayout();Pump();Check(callout.Visibility==Visibility.Visible,"locate restores current node callout");
    var escape=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(full),Environment.TickCount,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};full.RaiseEvent(escape);host.UpdateLayout();Pump();Check(escape.Handled&&!graph.IsFullscreen&&Visuals<TextBox>(host).Contains(reflectionDraft)&&reflectionDraft.Text=="全屏切换保留的未保存复述","Escape restores inline layout and preserves unsaved learning input");
    graph.EnterFullscreen();Pump();graph.SelectNode("attention");Pump();graph.ExitFullscreen();host.UpdateLayout();Pump();Check(!graph.IsFullscreen&&graph.ActiveDomain==tutorial.Id,"repeated fullscreen cycles retain active domain and allow node switching");
    var architectureFixture=RepositoryArchitectureTests.Fixture();pet.Archive.SaveDomainGraph(architectureFixture);graph.SelectDomain(architectureFixture.Id);host.UpdateLayout();Pump();
    Check(graph.ArchitectureView=="overview"&&Visuals<Button>(host).Exists(b=>b.Content as string=="展开模块 →"),"repository topic starts with module overview");
    Snapshot(host,"architecture-overview");graph.EnterFullscreen();Pump();foreach(Window w in Application.Current.Windows)if(w.Title=="曳尔沐 · 全屏学习")full=w;full.UpdateLayout();Pump();Snapshot(full,"architecture-fullscreen");
    Check(Visuals<Button>(full).Exists(b=>b.Content as string=="请求旅程"),"fullscreen retains architecture navigation");
    graph.SelectNode("@architecture/platform");Pump();Check(Visuals<TextBlock>(full).Exists(t=>t.Text=="注册与平台适配"),"module callout explains its responsibility");
    graph.OpenArchitectureModule("npu");full.UpdateLayout();Pump();Check(graph.ArchitectureModule=="npu"&&Visuals<Button>(full).Exists(b=>b.Content as string=="返回总览"),"module drilldown retains a route back to overview");Snapshot(full,"architecture-module");
    graph.SelectNode("worker-npu");full.UpdateLayout();Pump();Check(Visuals<TextBlock>(full).Exists(t=>new System.Windows.Documents.TextRange(t.ContentStart,t.ContentEnd).Text.Contains("本地打开")),"source nodes retain local-code navigation after drilldown");
    var architectureTabs=Visuals<TabControl>(full).Find(t=>t.Items.Count==4);architectureTabs.SelectedIndex=2;full.UpdateLayout();Pump();var architecturePractice=Visuals<Expander>(full).Find(e=>e.Header as string=="自测 · 用自己的话解释");architecturePractice.IsExpanded=true;full.UpdateLayout();Pump();var architectureDraft=Visuals<TextBox>(full).Find(t=>t.MaxLength==12000);architectureDraft.Text="切换架构视图也保留复述";
    graph.ShowArchitecture("journey");graph.MoveJourney(4);full.UpdateLayout();Pump();Check(graph.JourneyIndex==4&&graph.ArchitectureView=="journey","journey advances to NPU worker without confusing steps with graph edges");Snapshot(full,"architecture-journey");
    architectureTabs=Visuals<TabControl>(full).Find(t=>t.Items.Count==4);architectureTabs.SelectedIndex=2;full.UpdateLayout();Pump();architecturePractice=Visuals<Expander>(full).Find(e=>e.Header as string=="自测 · 用自己的话解释");architecturePractice.IsExpanded=true;full.UpdateLayout();Pump();Check(Visuals<TextBox>(full).Exists(t=>t.MaxLength==12000&&t.Text=="切换架构视图也保留复述"),"unsaved reflection survives switching architecture and journey views");
    graph.MoveJourney(100);Check(graph.JourneyIndex==7,"journey next step clamps at final response");graph.MoveJourney(-100);Check(graph.JourneyIndex==0,"journey previous step clamps at request entry");
    graph.ShowArchitecture("overview");full.UpdateLayout();Pump();Check(graph.ArchitectureModule==""&&graph.ArchitectureView=="overview","overview restores repository boundaries");graph.ExitFullscreen();host.UpdateLayout();Pump();
    Check(Visuals<Button>(host).Exists(b=>b.Content as string=="请求旅程"),"architecture navigation returns to the inline view on exit");
    graph.SelectDomain("test-domain");graph.SelectNode("mc2");host.UpdateLayout();Pump();var restoredTabs=Visuals<TabControl>(host).Find(delegate(TabControl t){return t.Items.Count==4&&((TabItem)t.Items[0]).Header as string=="概览";});restoredTabs.SelectedIndex=1;host.UpdateLayout();Pump();
    var selector=Visuals<ComboBox>(host)[0];selector.IsDropDownOpen=true;Pump();var dropdown=(Popup)selector.Template.FindName("PART_Popup",selector);dropdown.Child.UpdateLayout();
    Check(dropdown.IsOpen&&((FrameworkElement)dropdown.Child).ActualWidth>=selector.ActualWidth,"custom domain selector popup renders at field width");Snapshot((FrameworkElement)dropdown.Child,"modern-domain-selector");selector.IsDropDownOpen=false;Pump();
    var searchInput=Visuals<TextBox>(host).Find(delegate(TextBox t){return t.MaxLength==100;});searchInput.Text="ACLGraph";var searchDeadline=DateTime.UtcNow.AddMilliseconds(350);while(DateTime.UtcNow<searchDeadline){Pump();System.Threading.Thread.Sleep(10);}Pump();
    Check(Visuals<TextBox>(host).Exists(delegate(TextBox t){return t.IsReadOnly&&t.Text.Contains("--enforce-eager");}),"search selects a visible result and refreshes its inspector");
    var clearSearch=Visuals<Button>(host).Find(delegate(Button b){return b.ToolTip as string=="清除搜索";});Check(clearSearch.Visibility==Visibility.Visible,"modern search exposes clear action when populated");clearSearch.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(searchInput.Text.Length==0&&clearSearch.Visibility==Visibility.Hidden,"clear restores search placeholder without changing domain");
    CheckManualEditor(host);CheckAiGraphCreation(root);CheckStudyTools(pet,root);CheckModuleHierarchy(root);
    // Close while a new graph layout is pending; late completion must not touch disposed controls.
    pet.Archive.SaveKnowledgeNode("","关闭前新增节点","测试","待核实","了解","测试","");graph.Dispose();host.Content=button;graph=null;
    for(int i=0;i<20;i++){Pump();System.Threading.Thread.Sleep(10);}Check(dispatcherErrors.Count==0,"closing graph during layout produces no deferred UI exception");
    // Real plugin management page, with install state isolated from preview/user storage.
    bar.Close();bar=null;panel.Close();panel=null;
    string pluginFolder=Path.Combine(root,"plugin-ui"),appFolder=AppDomain.CurrentDomain.BaseDirectory;
    pet.State.Plugins=new PluginManager(pluginFolder,appFolder,false);
    panel=new ControlWindow(pet);panel.ShowActivated=false;panel.Show();panel.SelectTab(PanelTab.Plugins);Pump();
    Check(Visuals<Button>(panel).FindAll(b=>b.Content as string=="安装").Count==5,"fresh manager exposes five optional installs");Snapshot(panel,"plugins-uninstalled");
    panel.SelectTab(PanelTab.Graph);Pump();Check(Visuals<Button>(panel).Exists(b=>b.Content as string=="管理插件")&&!Visuals<Button>(panel).Exists(b=>b.Content as string=="编辑节点"),"uninstalled graph route shows install guidance without creating graph UI");
    panel.SelectTab(PanelTab.Plugins);Pump();Visuals<Button>(panel).Find(b=>b.Content as string=="安装").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
    Check(pet.State.Plugins.Installed("archive")&&!pet.State.Plugins.Enabled("archive")&&Visuals<Button>(panel).Exists(b=>b.Content as string=="重启曳尔沐并应用"),"install button persists package and offers restart");Snapshot(panel,"plugins-pending-restart");
    panel.Close();panel=null;pet.State.Plugins=new PluginManager(pluginFolder,appFolder,false);
    panel=new ControlWindow(pet);panel.ShowActivated=false;panel.Show();panel.SelectTab(PanelTab.Archive);Pump();
    Check(Visuals<TabControl>(panel).Exists(t=>t!=panel.Tabs&&t.Items.Count==4),"installed archive page loads through module contract after restart");
    panel.SelectTab(PanelTab.Companion);Pump();Check(Visuals<Button>(panel).Exists(b=>b.Content as string=="浮动输入框"),"core companion remains available with other plugins absent");
    Check(!Directory.Exists(Storage.Folder)||Directory.GetFiles(Storage.Folder,"*",SearchOption.AllDirectories).Length==0,"UI tests do not write user settings or archive data");
    report.AppendLine("PASS popup checks="+checks+" real popups opened="+opened+" distinct application hints="+samples.Count);
   }catch(Exception e){report.AppendLine("FAIL "+e);throw;}
   finally {
    if(graph!=null)graph.Dispose();if(bar!=null)bar.Close();if(panel!=null)panel.Close();if(pet!=null)pet.Close();host.Close();
    Directory.CreateDirectory("tests");File.WriteAllText("tests/popup-tests.txt",report.ToString());Storage.Folder=previous;
    app.Shutdown();
   }
   Console.WriteLine(report.ToString());
  }
  static void CheckModuleHierarchy(string root){
   using(var h=new GraphAiCreationTests.Harness(true,root)){
    var graph=new KnowledgeGraphPanel(h.Service,delegate{});var host=new Window{Width=1000,Height=900,ShowInTaskbar=false,ShowActivated=false,Content=new ScrollViewer{Content=graph.Root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
    try{
     host.Show();host.UpdateLayout();Pump();graph.OpenArchitectureModule("npu");h.Api.Result=GraphEditing.Json(ModuleHierarchyTests.Split(h.Graph));GraphEditOutcome hierarchyResult=null;h.Service.EditGraphWithAi("建立子模块",h.Graph.Id,"","npu",r=>{hierarchyResult=r;graph.PresentGraphEdit(r);});h.Queue.Run();host.UpdateLayout();Pump();
     Check(graph.ArchitectureModule=="npu"&&Visuals<TextBlock>(host).Exists(t=>new System.Windows.Documents.TextRange(t.ContentStart,t.ContentEnd).Text=="模块 · 模型执行"),"module-only AI result navigates to its parent and exposes module links: "+graph.ArchitectureModule+" / "+(hierarchyResult==null?"no result":hierarchyResult.Message));
     var child=Visuals<Border>(host).Find(b=>b.Tag as string=="@architecture/npu-model");var source=Visuals<Border>(host).Find(b=>b.Tag as string=="worker-npu");
     Check(child!=null&&source!=null&&Visuals<Border>(host).Exists(b=>b.Tag as string=="module-relation"),"parent view mixes direct source nodes and child modules with projected relationships");
     var stripe=Visuals<Border>(child).Find(b=>b.Background is SolidColorBrush&&((SolidColorBrush)b.Background).Color==(Color)ColorConverter.ConvertFromString("#C597FF"));Check(stripe!=null,"child module keeps the A-tier bottom stripe from descendant nodes");
     Visuals<Button>(child).Find(b=>b.Content as string=="展开模块 →").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));host.UpdateLayout();Pump();Check(graph.ArchitectureModule=="npu-model","expand button drills down one level");
     var grandchild=Visuals<Border>(host).Find(b=>b.Tag as string=="@architecture/npu-forward");var sibling=Visuals<Border>(host).Find(b=>b.Tag as string=="@architecture/npu-input");Check(grandchild!=null&&sibling!=null&&Visuals<Border>(host).Find(b=>b.Tag as string=="ascend-model-runner")==null,"second level shows direct submodules and hides deeper source cards");
     var first=new Rect(grandchild.TranslatePoint(new Point(),host),grandchild.RenderSize);var second=new Rect(sibling.TranslatePoint(new Point(),host),sibling.RenderSize);Check(!first.IntersectsWith(second)&&Visuals<TextBlock>(sibling).Exists(t=>t.Text.Contains("较长的模块名称")&&t.MaxHeight==48),"sibling cards do not overlap and long names use bounded wrapping");Snapshot(host,"hierarchy-window");
     graph.EnterFullscreen();Pump();Window full=null;foreach(Window w in Application.Current.Windows)if(w.Title=="曳尔沐 · 全屏学习")full=w;full.UpdateLayout();Pump();
     grandchild=Visuals<Border>(full).Find(b=>b.Tag as string=="@architecture/npu-forward");Check(!Visuals<Button>(grandchild).Exists(b=>b.Content as string=="展开模块 →"),"single-node leaf card has no redundant expand action");Visuals<Button>(grandchild).Find(b=>b.Content is StackPanel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));full.UpdateLayout();Pump();
     Check(graph.ArchitectureModule=="npu-model"&&Visuals<TabControl>(full).Exists(t=>t.IsVisible&&t.Items.Count==4)&&Visuals<TextBlock>(full).Exists(t=>new System.Windows.Documents.TextRange(t.ContentStart,t.ContentEnd).Text=="本地打开"),"single leaf click opens visible source details and code actions without leaving the flow");
     Check(h.Service.CaptureGraphReading("").NodeId=="ascend-model-runner"&&h.Service.CaptureGraphReading("").ModuleId=="npu-forward","single leaf consultation uses the real source node and module");Snapshot(full,"single-leaf-direct-read");
     graph.OpenArchitectureModule("npu-forward");full.UpdateLayout();Pump();Check(graph.ArchitectureModule=="npu-model","module navigation also skips redundant single-node leaf levels");
     Check(h.Service.CaptureGraphReading("这里").Text.Contains("NPU 执行层 › 模型执行 › 前向与输出处理"),"consultation follows the same multi-level path as the reader");
     Visuals<Button>(full).Find(b=>b.Content as string=="返回上级").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));full.UpdateLayout();Pump();Check(graph.ArchitectureModule=="npu","back action returns to the parent of the visible flow");
     graph.OpenArchitectureModule("npu-input");full.UpdateLayout();Pump();Check(Visuals<TextBlock>(full).Exists(t=>t.Text.Contains("模块暂时为空")),"empty submodule presents an explicit action hint");
     graph.OpenArchitectureModule("npu-model");((ReadingZoom)full.Content).SetFactor(1.35);full.UpdateLayout();Pump();Snapshot(full,"hierarchy-fullscreen-zoom");
     ClickGraphLink(full,"NPU 执行层");full.UpdateLayout();Pump();Check(graph.ArchitectureModule=="npu","breadcrumb can jump directly to an ancestor");
     graph.SelectNode("ascend-model-runner");full.UpdateLayout();Pump();Check(graph.ArchitectureModule=="npu-model"&&h.Service.CaptureGraphReading("").NodeId=="ascend-model-runner","cross-module source selection opens the leaf card in its parent flow");
     graph.ShowArchitecture("overview");full.UpdateLayout();Pump();Check(Visuals<Border>(full).Exists(b=>b.Tag as string=="@architecture/npu")&&!Visuals<Border>(full).Exists(b=>b.Tag as string=="@architecture/npu-model"),"root view shows only top-level modules");
     graph.ShowArchitecture("journey");graph.PresentGraphEdit(new GraphEditOutcome{Success=true,SlotId=h.Service.Slot.Id,TopicId=h.Graph.Id,UpdatedModuleIds=new List<string>{"npu"}});full.UpdateLayout();Pump();Check(graph.ArchitectureView=="overview"&&graph.ArchitectureModule==""&&Visuals<Border>(full).Exists(b=>b.Tag as string=="@architecture/npu"),"module result navigation safely leaves the journey and rebuilds the root scene");
     graph.OpenArchitectureModule("npu-model");graph.ExitFullscreen();host.UpdateLayout();Pump();Check(graph.ArchitectureModule=="npu-model"&&Visuals<Border>(host).Exists(b=>b.Tag as string=="@architecture/npu-forward"),"leaving fullscreen preserves the current module depth");
     grandchild=Visuals<Border>(host).Find(b=>b.Tag as string=="@architecture/npu-forward");Visuals<Button>(grandchild).Find(b=>b.Content is StackPanel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));host.UpdateLayout();Pump();Check(graph.ArchitectureModule=="npu-model"&&Visuals<TabControl>(host).Exists(t=>t.IsVisible&&t.Items.Count==4),"inline leaf card also opens source details directly");
     var changed=DomainKnowledge.Clone(h.Graph);changed.Nodes.Find(n=>n.Id=="ascend-model-runner").Summary="更新后的原节点说明";h.Service.SaveDomainGraph(changed);host.UpdateLayout();Pump();Check(graph.ArchitectureModule=="npu-model"&&Visuals<TextBlock>(host).Exists(t=>t.Text=="更新后的原节点说明"),"source updates refresh direct leaf details without changing the visible level");
    }finally{graph.Dispose();host.Close();}
   }
  }
  static void CheckStudyTools(PetWindow pet,string root){
   using(var h=new GraphAiCreationTests.Harness(true,root)){
    var graph=new KnowledgeGraphPanel(h.Service,delegate{},pet.Conversation);var host=new Window{Width=1280,Height=900,ShowInTaskbar=false,ShowActivated=false,Content=graph.Root};
    bool online=pet.State.AiChat;var oldGraphs=pet.Conversation.Graphs;
    try{
     pet.State.AiChat=false;pet.Conversation.Graphs=h.Service;host.Show();host.UpdateLayout();Pump();
     var question=Visuals<TextBox>(host).Find(t=>t.Tag as string=="graph-consult-input");
     graph.OpenArchitectureModule("npu");graph.SelectNode("worker-npu");question.Text="输入草稿";graph.EnterFullscreen();Pump();Window full=null;foreach(Window w in Application.Current.Windows)if(w.Title=="曳尔沐 · 全屏学习")full=w;full.UpdateLayout();Pump();
     Check(Visuals<TextBox>(full).Contains(question)&&question.Text=="输入草稿","fullscreen preserves consultation input");
     var snapshot=h.Service.CaptureGraphReading("这里是什么");Check(snapshot.HasView&&snapshot.ModuleId=="npu"&&snapshot.NodeId=="worker-npu"&&snapshot.CodeUrls.Count>0&&snapshot.Text.Contains("说明页签："),"top consultation reads the visible node, module, page and code context");
     question.Text="本页的作用是什么";int before=pet.Conversation.Lines.Length;Visuals<Button>(full).Find(b=>b.Content as string=="问曳尔沐").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));full.UpdateLayout();Pump();
     Check(pet.Conversation.Lines.Length>before&&question.Text==""&&Visuals<TextBlock>(full).Exists(t=>t.Text.StartsWith("曳尔沐：")),"consultation submits through the shared chat and displays its offline reply");
     Check(Visuals<Expander>(full).Find(e=>e.Header as string=="咨询记录 · 与陪伴对话同步").IsExpanded,"sending expands a bounded answer area");
     Snapshot(full,"graph-study-tools");graph.ExitFullscreen();host.UpdateLayout();Pump();Check(Visuals<TextBox>(host).Contains(question),"leaving fullscreen restores the consultation input");
     question.Text="待剪切";question.SelectAll();System.Windows.Input.ApplicationCommands.Cut.Execute(null,question);Check(question.Text=="","top consultation supports Ctrl+X cut command");
    }finally{pet.State.AiChat=online;pet.Conversation.Graphs=oldGraphs;graph.Dispose();host.Close();}
   }
  }
  static void CheckAiGraphCreation(string root){
   using(var h=new GraphAiCreationTests.Harness(true,root)){
    var graph=new KnowledgeGraphPanel(h.Service,delegate{});var host=new Window{Title="AI 图谱测试",Width=1280,Height=900,ShowInTaskbar=false,ShowActivated=false,Content=graph.Root};
    try{
     host.Show();host.UpdateLayout();Pump();graph.OpenArchitectureModule("npu");graph.SelectNode("worker-npu");graph.EnterFullscreen();Pump();Window full=null;foreach(Window w in Application.Current.Windows)if(w.Title=="曳尔沐 · 全屏学习")full=w;full.UpdateLayout();Pump();h.SetAddResponse("from-ui-one","from-ui-two");
     Check(Visuals<Border>(full).Exists(b=>b.Tag as string=="module-relation"),"module drilldown renders stored relationships instead of isolated cards");
     var grip=Visuals<Thumb>(full).Find(t=>t.ToolTip as string=="拖动说明框");Check(grip!=null&&grip.IsVisible,"fullscreen inspector has a dedicated drag handle");var frame=Visuals<Border>(full).Find(b=>b.BorderThickness.Left==2&&b.Child is DockPanel);double beforeX=Canvas.GetLeft(frame),beforeY=Canvas.GetTop(frame);grip.RaiseEvent(new DragDeltaEventArgs(-100,-80){RoutedEvent=Thumb.DragDeltaEvent});full.UpdateLayout();Pump();Check(Canvas.GetLeft(frame)!=beforeX||Canvas.GetTop(frame)!=beforeY,"drag handle repositions the inspector");double movedX=Canvas.GetLeft(frame);full.UpdateLayout();Pump();Check(Canvas.GetLeft(frame)==movedX,"deferred positioning preserves manual placement");graph.MoveDetails(-100000,-100000);Check(Canvas.GetLeft(frame)==12&&Canvas.GetTop(frame)==12,"manual dragging clamps the inspector to the visible area");graph.SelectNode("ascend-model-runner");graph.SelectNode("worker-npu");full.UpdateLayout();Pump();Check(Canvas.GetLeft(frame)!=12||Canvas.GetTop(frame)!=12,"a different selected node restores automatic placement");
     var cutBox=new TextBox{Text="前缀 需要剪切 后缀",IsUndoEnabled=true};string copied="";var cutWindow=new Window{Content=cutBox,Width=320,Height=100,ShowInTaskbar=false,ShowActivated=false};cutWindow.Show();cutWindow.UpdateLayout();cutBox.IsUndoEnabled=false;cutBox.IsUndoEnabled=true;ClipboardEditing.Attach(cutBox,value=>{copied=value;return true;});cutBox.Select(3,4);Check(System.Windows.Input.ApplicationCommands.Cut.CanExecute(null,cutBox),"editable input enables Ctrl+X for selected text");System.Windows.Input.ApplicationCommands.Cut.Execute(null,cutBox);Check(copied=="需要剪切"&&cutBox.Text=="前缀  后缀","cut command copies precisely the selection before deleting it");cutBox.Undo();Check(cutBox.Text=="前缀 需要剪切 后缀","cut remains undoable");cutBox.Select(3,4);Check(!ClipboardEditing.Cut(cutBox,value=>false)&&cutBox.Text=="前缀 需要剪切 后缀","clipboard failure preserves selected input");cutBox.IsReadOnly=true;Check(!ClipboardEditing.Cut(cutBox,value=>true),"read-only messages cannot be cut");cutWindow.Close();
     var reading=h.Service.CaptureGraphReading("这个节点的职责");var view=graph.CaptureReadingView();Check(view.Fullscreen&&view.Mode=="module"&&view.NodeId=="worker-npu"&&view.Visible.Contains("worker-npu")&&reading.ModuleId=="npu","fullscreen conversation captures selected source and visible card from the live view");
     Visuals<Button>(full).Find(b=>b.ToolTip as string=="收起节点说明").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));full.UpdateLayout();Pump();view=graph.CaptureReadingView();Check(!view.DetailsVisible&&view.NodeId=="worker-npu","dismissing details preserves the conversational reference without claiming the panel is open");graph.SelectNode("worker-npu");full.UpdateLayout();Pump();
     var copyTitle=Visuals<GraphCopyText>(full).Find(t=>t.CopyText=="NPUWorker：执行入口");Check(copyTitle!=null,"node text exposes selection without changing its TextBlock layout");copyTitle.Focus();copyTitle.SelectAll();full.UpdateLayout();Pump();Check(copyTitle.SelectedText==copyTitle.CopyText&&System.Windows.Input.ApplicationCommands.Copy.CanExecute(null,copyTitle),"graph text supports select-all and the standard copy command");Snapshot(full,"graph-copy-selection");
     var longText=new GraphCopyText{Text="完整文字不会被省略号截断",MaxWidth=50,TextTrimming=TextTrimming.CharacterEllipsis};Check(longText.CopyText=="完整文字不会被省略号截断"&&((MenuItem)longText.ContextMenu.Items[1]).Header as string=="复制全部文字","context menu exposes complete underlying text, including clipped card content");copyTitle.ClearSelection();
     var sourceCopy=Visuals<GraphCopyText>(full).Find(t=>t.ContextMenu.Items.Count==4);Check(sourceCopy!=null&&((MenuItem)sourceCopy.ContextMenu.Items[3]).Header as string=="复制链接地址","source links offer copying the URL while retaining normal navigation");
     string originalSummary=h.Graph.Nodes.Find(n=>n.Id=="worker-npu").Summary;h.Api.Result=GraphEditing.Json(new NodeReadingGuide{Terms=new List<GuideTerm>{new GuideTerm{Term="NPUWorker",Meaning="管理设备并接收模型执行任务的组件。"}},Steps=new List<GuideStep>{new GuideStep{Original=originalSummary,Explanation="当前测试未读取实现，不提供代码跳转。"}}});int rewriteCalls=h.Api.Calls;ClickGraphLink(full,"请曳尔沐翻译术语");Check(h.Service.Busy,"terminology action submits an AI translation from the current inspector");h.Queue.Run();full.UpdateLayout();Pump();Check(h.Api.Calls==rewriteCalls+2&&h.Graph.Nodes.Find(n=>n.Id=="worker-npu").Summary==originalSummary&&Visuals<TabControl>(full).Exists(t=>t.Items.Count==3),"translation preserves the original description and exposes separate learning tabs");var guideTabs=Visuals<TabControl>(full).Find(t=>t.Items.Count==3);Check(guideTabs.SelectedIndex==1&&Visuals<GraphCopyText>(full).Exists(t=>t.CopyText=="NPUWorker"),"term definitions are visible after translation rather than hidden in implementation details");guideTabs.SelectedIndex=2;full.UpdateLayout();Pump();Check(Visuals<TextBlock>(full).Exists(t=>t.Text=="对应源码尚未定位"),"unverified steps visibly report missing code mapping");Snapshot(full,"terminology-flow-guide");h.SetAddResponse("from-ui-one","from-ui-two");
     Exception promptFailure=null;
     Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{
      Window prompt=null;foreach(Window w in Application.Current.Windows)if(w.Title=="让曳尔沐编辑图谱")prompt=w;
      try{Check(prompt!=null&&prompt.Owner==full,"AI node request dialog stays above the fullscreen graph");prompt.UpdateLayout();Check(Visuals<TextBlock>(prompt).Exists(t=>t.Text.Contains("新增节点")),"AI request dialog explains node creation");Visuals<TextBox>(prompt).Find(t=>t.MaxLength==2000).Text="在当前模块新增两个节点，并关联当前节点。";Snapshot(prompt,"ai-node-request");Visuals<Button>(prompt).Find(b=>b.Content as string=="整理并保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
      catch(Exception ex){promptFailure=ex;}finally{if(prompt!=null&&prompt.IsVisible)prompt.Close();}
     }));
     ClickGraphLink(full,"请曳尔沐修改节点");if(promptFailure!=null)throw promptFailure;Check(h.Service.Busy,"fullscreen node action submits the scoped AI task");h.Queue.Run();full.UpdateLayout();Pump();
     Check(graph.IsFullscreen&&graph.ArchitectureModule=="npu"&&h.Graph.Nodes.Exists(n=>n.Id=="from-ui-one"),"AI additions persist and automatically open their module without leaving fullscreen");
     Check(Visuals<TextBlock>(full).Exists(t=>t.Text=="本次新增 · ")&&Visuals<TextBlock>(full).Exists(t=>t.Text=="新增学习节点 from-ui-one"),"saved AI result offers navigation and shows the first added node");Snapshot(full,"ai-node-created");
     ClickGraphLink(full,"新增学习节点 from-ui-two");full.UpdateLayout();Pump();Check(Visuals<TextBlock>(full).Exists(t=>t.Text=="新增学习节点 from-ui-two"),"each added node can be opened from the result links");
     graph.ShowArchitecture("overview");graph.SelectNode("@architecture/npu");full.UpdateLayout();Pump();Check(Visuals<TextBlock>(full).Exists(t=>new System.Windows.Documents.TextRange(t.ContentStart,t.ContentEnd).Text=="请曳尔沐新增节点"),"module callout also exposes AI node creation");
     view=graph.CaptureReadingView();Check(view.Mode=="overview"&&view.ModuleId=="npu"&&view.NodeId=="","module callout synchronizes module identity rather than a fabricated node");
     graph.ShowArchitecture("journey");graph.MoveJourney(6);full.UpdateLayout();Pump();view=graph.CaptureReadingView();Check(view.JourneyIndex==6&&view.Visible.Exists(id=>id.StartsWith("步骤7:")),"journey context captures the active step even when its node appears twice");Check(reading.NodeId=="worker-npu"&&reading.ModuleId=="npu","captured question keeps its original target while the user explores other views");
     var other=DomainGraphTests.Fixture();h.Service.SaveDomainGraph(other);h.SetAddResponse("late-result");h.Service.EditGraphWithAi("新增",h.Graph.Id,"worker-npu","npu",r=>graph.PresentGraphEdit(r));graph.SelectDomain(other.Id);h.Queue.Run();Pump();Check(graph.ActiveDomain==other.Id,"late AI completion does not switch away from another topic");
     graph.ExitFullscreen();host.UpdateLayout();Pump();Check(dispatcherErrors.Count==0,"AI result navigation and fullscreen exit produce no deferred UI exceptions");
    }finally{graph.Dispose();Check(h.Service.ReadingView==null,"disposing the graph releases its live reading context");host.Close();}
   }
  }
  static void ClickGraphLink(DependencyObject root,string label){
   foreach(var text in Visuals<TextBlock>(root))foreach(var inline in text.Inlines){var link=inline as System.Windows.Documents.Hyperlink;if(link!=null&&new System.Windows.Documents.TextRange(link.ContentStart,link.ContentEnd).Text==label){link.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));return;}}
   throw new Exception("Missing graph link: "+label);
  }
  static void CheckManualEditor(Window owner){
   var sample=new DomainGraph{Id="manual",Title="手动编辑验证"};sample.Nodes.Add(new DomainNode{Id="one",Title="节点一",Summary="介绍",Code="keep code"});sample.Nodes.Add(new DomainNode{Id="two",Title="节点二",Summary="介绍"});sample.Edges.Add(new DomainEdge{From="two",To="one",Label="输入"});
   int writes=0;DomainGraph saved=null;
   string result=DriveEditor(owner,sample,"one",delegate(Window editor){
    Check(Visuals<TextBlock>(editor).Exists(delegate(TextBlock t){return t.Text.Contains("节点二 — 输入 → 当前节点");}),"manual editor displays incoming relationships");
    Visuals<TextBox>(editor).Find(delegate(TextBox t){return t.MaxLength==120;}).Text="手动更新";
    Visuals<Button>(editor).Find(delegate(Button b){return b.ToolTip as string=="移除关联";}).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    var choices=Visuals<ComboBox>(editor);choices[1].SelectedIndex=1;choices[2].SelectedIndex=0;
    Visuals<Button>(editor).Find(delegate(Button b){return b.Content as string=="添加关联";}).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Snapshot(editor,"manual-graph-editor");var save=Visuals<Button>(editor).Find(delegate(Button b){return b.Content as string=="保存节点";});save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(editor.IsVisible&&Visuals<TextBlock>(editor).Exists(delegate(TextBlock t){return t.Text=="模拟保存失败";})&&sample.Nodes[0].Title=="节点一","failed save retains dialog and leaves original graph unchanged");save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   },delegate(DomainGraph candidate){writes++;if(writes==1)return "模拟保存失败";saved=DomainKnowledge.Clone(candidate);return "";});
   Check(result=="one"&&writes==2&&saved.Nodes[0].Title=="手动更新"&&saved.Nodes[0].Code=="keep code"&&saved.Edges.Count==1&&saved.Edges[0].From=="two"&&saved.Edges[0].To=="one"&&saved.Edges[0].Label=="依赖","retry saves edited content and replaces incoming relationship without duplicates");
   string before=GraphEditing.Revision(sample);result=DriveEditor(owner,sample,"one",delegate(Window editor){Visuals<TextBox>(editor).Find(delegate(TextBox t){return t.MaxLength==120;}).Text="取消草稿";editor.Close();},delegate(DomainGraph candidate){throw new Exception("cancel must not persist");});Check(result==null&&GraphEditing.Revision(sample)==before,"cancel leaves graph content and relationships unchanged");
   result=DriveEditor(owner,sample,null,delegate(Window editor){Visuals<TextBox>(editor).Find(delegate(TextBox t){return t.MaxLength==120;}).Text="新增节点";Visuals<TextBox>(editor).Find(delegate(TextBox t){return t.MaxLength==6000;}).Text="新介绍";Visuals<Button>(editor).Find(delegate(Button b){return b.Content as string=="保存节点";}).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate(DomainGraph candidate){return "";});Check(result!=null&&sample.Nodes.Count==3&&sample.Nodes[2].Summary=="新介绍","manual editor creates a new node with real content");
  }
  static string DriveEditor(Window owner,DomainGraph graph,string id,Action<Window> action,Func<DomainGraph,string> persist){
   Exception failure=null;Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{Window editor=null;foreach(Window w in Application.Current.Windows)if(w.Title=="编辑知识节点"||w.Title=="新增知识节点")editor=w;try{if(editor==null)throw new Exception("Editor did not open");editor.UpdateLayout();action(editor);}catch(Exception ex){failure=ex;}finally{if(editor!=null&&editor.IsVisible)editor.Close();}}));string result=DomainGraphEditor.Edit(owner,graph,id,persist);if(failure!=null)throw failure;return result;
  }
  static void CollectTips(DependencyObject root,HashSet<string> samples){foreach(var e in Visuals<FrameworkElement>(root)){string text=e.ToolTip as string;if(!string.IsNullOrEmpty(text))samples.Add(text);}}
  static bool ExistsText(this HashSet<string> values,string value){foreach(var text in values)if(text.Contains(value))return true;return false;}
 }
}
