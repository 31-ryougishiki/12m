using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel {
  StackPanel fullscreenHeader;Window fullscreenWindow;Grid fullscreenStage;Canvas fullscreenOverlay;Button dismissDetails;
  bool fullDetailsVisible,positionQueued;
  System.Windows.Controls.Primitives.Thumb detailsGrip;Point? manualDetailsPosition;string manualDetailsKey="";
  string DetailsKey {get{return ActiveDomain+"/"+architectureMode+"/"+journeyIndex+"/"+selected;}}
  UIElement CreateDetailsGrip(){detailsGrip=new System.Windows.Controls.Primitives.Thumb{Width=28,Height=28,Cursor=Cursors.SizeAll,ToolTip="拖动说明框",Visibility=Visibility.Collapsed};detailsGrip.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Thumb\"><Border Background=\"Transparent\"><TextBlock Text=\"⠿\" FontSize=\"22\" Foreground=\"#5368B2\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Border></ControlTemplate>");detailsGrip.DragDelta+=delegate(object s,System.Windows.Controls.Primitives.DragDeltaEventArgs e){MoveDetails(e.HorizontalChange,e.VerticalChange);e.Handled=true;};return detailsGrip;}
  public void MoveDetails(double dx,double dy){if(!IsFullscreen||!detailFrame.IsVisible)return;manualDetailsKey=DetailsKey;manualDetailsPosition=new Point(Canvas.GetLeft(detailFrame)+dx,Canvas.GetTop(detailFrame)+dy);PositionFullscreenDetails();}
  public static Rect ClampDetails(Point at,Size card,Size area){double w=Math.Min(card.Width,Math.Max(1,area.Width-24)),h=Math.Min(card.Height,Math.Max(1,area.Height-24));return new Rect(Math.Max(12,Math.Min(at.X,area.Width-12-w)),Math.Max(12,Math.Min(at.Y,area.Height-12-h)),w,h);}
  public bool IsFullscreen {get{return fullscreenWindow!=null;}}
  Window LearningOwner {get{return fullscreenWindow??Window.GetWindow(Root);}}
  public void EnterFullscreen(){
   if(disposed||IsFullscreen)return;
   var owner=Window.GetWindow(Root);if(owner==null)return;
   var window=new Window{Owner=owner,Title="曳尔沐 · 全屏学习",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Theme.Brush("#F2F4FA"),ShowInTaskbar=false,UseLayoutRounding=true};
   var bounds=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(owner).Handle).Bounds;var source=PresentationSource.FromVisual(owner);var matrix=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;var origin=matrix.Transform(new Point(bounds.Left,bounds.Top));var size=matrix.Transform(new Vector(bounds.Width,bounds.Height));window.Left=origin.X;window.Top=origin.Y;window.Width=size.X;window.Height=size.Y;
   fullscreenWindow=window;detailsGrip.Visibility=Visibility.Visible;manualDetailsPosition=null;fullDetailsVisible=Selected()!=null||SelectedModule()!=null;
   var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   var toolbar=new DockPanel{Margin=new Thickness(14,12,14,12)};var exit=EditAction("退出全屏 · Esc",ExitFullscreen);DockPanel.SetDock(exit,Dock.Right);toolbar.Children.Add(exit);var reset=EditAction("定位当前节点",delegate{manualDetailsPosition=null;CenterSelected();fullDetailsVisible=true;RequestFullscreenPosition();});DockPanel.SetDock(reset,Dock.Right);toolbar.Children.Add(reset);var title=Text("",22,"#28324A");title.SetBinding(TextBlock.TextProperty,new Binding("Text"){Source=heading});title.TextTrimming=TextTrimming.CharacterEllipsis;title.TextWrapping=TextWrapping.NoWrap;toolbar.Children.Add(title);fullscreenHeader=new StackPanel();fullscreenHeader.Children.Add(toolbar);graphArea.Children.Remove(studyTools);studyTools.Margin=new Thickness(14,0,14,8);fullscreenHeader.Children.Add(studyTools);graphArea.Children.Remove(architectureBar);architectureBar.Margin=new Thickness(14,0,14,8);fullscreenHeader.Children.Add(architectureBar);graphArea.Children.Remove(graphEditResultsHost);graphEditResultsHost.Margin=new Thickness(14,0,14,0);fullscreenHeader.Children.Add(graphEditResultsHost);layout.Children.Add(fullscreenHeader);
   fullscreenStage=new Grid{ClipToBounds=true,Margin=new Thickness(14,0,14,0),Background=canvas.Background};Grid.SetRow(fullscreenStage,1);layout.Children.Add(fullscreenStage);
   graphFrame.Children.Remove(viewport);body.Children.Remove(detailFrame);viewport.Height=double.NaN;fullscreenStage.Children.Add(viewport);
   fullscreenOverlay=new Canvas();fullscreenStage.Children.Add(fullscreenOverlay);detailFrame.Margin=new Thickness(0);detailFrame.BorderBrush=Theme.Brush("#5368B2");detailFrame.BorderThickness=new Thickness(2);fullscreenOverlay.Children.Add(detailFrame);dismissDetails.Visibility=Visibility.Visible;
   var footer=new StackPanel{Margin=new Thickness(14,8,14,0)};footer.Children.Add(Text("点击节点查看说明 · 点击框外收起 · 拖动空白平移 · 滚轮缩放图谱 · Ctrl + 滚轮调整字号 · 拖选文字 Ctrl+C · 右键复制全文 · 对话同步当前图谱",12,"#687794"));var message=Text("",12,"#247A7B");message.SetBinding(TextBlock.TextProperty,new Binding("Text"){Source=status});footer.Children.Add(message);Grid.SetRow(footer,2);layout.Children.Add(footer);
   var control=owner as ControlWindow;window.Content=new ReadingZoom(window,layout,control==null?1:control.ReadingView.Factor,delegate(double value){if(control!=null)control.ReadingView.SetFactor(value);RequestFullscreenPosition();});
   window.PreviewKeyDown+=FullscreenKey;window.PreviewMouseDown+=FullscreenOutsideClick;window.Closed+=FullscreenClosed;viewport.ScrollChanged+=FullscreenScroll;fullscreenStage.SizeChanged+=FullscreenSize;
   GraphCopyText.Enable(window);
   window.Show();window.UpdateLayout();CenterSelected();RequestFullscreenPosition();
  }
  void FullscreenKey(object sender,KeyEventArgs e){if(e.Key==Key.Escape||e.Key==Key.F11){ExitFullscreen();e.Handled=true;}}
  void FullscreenOutsideClick(object sender,MouseButtonEventArgs e){
   if(e.ChangedButton==MouseButton.Right)return;
   for(var current=e.OriginalSource as DependencyObject;current!=null;current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current))if(current==detailFrame)return;
   // Do not consume the click: nodes can open their own details and the canvas can still pan.
   fullDetailsVisible=false;PositionFullscreenDetails();
  }
  void FullscreenScroll(object sender,ScrollChangedEventArgs e){RequestFullscreenPosition();}
  void FullscreenSize(object sender,SizeChangedEventArgs e){RequestFullscreenPosition();}
  public void ExitFullscreen(){if(fullscreenWindow!=null)fullscreenWindow.Close();}
  void FullscreenClosed(object sender,EventArgs e){
   var window=(Window)sender;window.Closed-=FullscreenClosed;window.PreviewKeyDown-=FullscreenKey;window.PreviewMouseDown-=FullscreenOutsideClick;viewport.ScrollChanged-=FullscreenScroll;fullscreenStage.SizeChanged-=FullscreenSize;
   double x=(viewport.HorizontalOffset+viewport.ViewportWidth/2)/scale.ScaleX,y=(viewport.VerticalOffset+viewport.ViewportHeight/2)/scale.ScaleY;
   fullscreenHeader.Children.Remove(graphEditResultsHost);graphArea.Children.Insert(0,graphEditResultsHost);graphEditResultsHost.Margin=new Thickness(0);fullscreenHeader.Children.Remove(architectureBar);graphArea.Children.Insert(0,architectureBar);architectureBar.Margin=new Thickness(0,0,0,10);fullscreenHeader.Children.Remove(studyTools);graphArea.Children.Insert(0,studyTools);studyTools.Margin=new Thickness(0,0,0,12);fullscreenHeader=null;fullscreenOverlay.Children.Remove(detailFrame);fullscreenStage.Children.Remove(viewport);fullscreenWindow=null;fullscreenOverlay=null;fullscreenStage=null;
   viewport.Height=540;graphFrame.Children.Insert(0,viewport);body.Children.Add(detailFrame);detailFrame.Width=double.NaN;detailFrame.Height=double.NaN;detailFrame.Visibility=Visibility.Visible;detailFrame.BorderBrush=Theme.Brush("#8996B5");detailFrame.BorderThickness=new Thickness(1);dismissDetails.Visibility=Visibility.Collapsed;detailsGrip.Visibility=Visibility.Collapsed;manualDetailsPosition=null;ArrangeBody(((FrameworkElement)Root).ActualWidth>=1100);
   if(!disposed){((FrameworkElement)Root).UpdateLayout();viewport.ScrollToHorizontalOffset(Math.Max(0,x*scale.ScaleX-viewport.ViewportWidth/2));viewport.ScrollToVerticalOffset(Math.Max(0,y*scale.ScaleY-viewport.ViewportHeight/2));}
  }
  void RequestFullscreenPosition(){
   if(!IsFullscreen||positionQueued||disposed||canvas.Dispatcher.HasShutdownStarted)return;positionQueued=true;
   canvas.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate{positionQueued=false;if(!disposed)PositionFullscreenDetails();}));
  }
  void PositionFullscreenDetails(){
   if(!IsFullscreen)return;
   var node=scene==null?null:scene.Nodes.Find(delegate(GraphNodePlacement p){return p.Item.Id==selected;});
   double width=fullscreenStage.ActualWidth-18,height=fullscreenStage.ActualHeight-18;
   if(!fullDetailsVisible||node==null||width<100||height<100){detailFrame.Visibility=Visibility.Hidden;return;}
   if(manualDetailsKey!=DetailsKey)manualDetailsPosition=null;
   if(manualDetailsPosition.HasValue){var moved=ClampDetails(manualDetailsPosition.Value,new Size(420,640),new Size(width,height));manualDetailsPosition=moved.TopLeft;detailFrame.Width=moved.Width;detailFrame.Height=moved.Height;Canvas.SetLeft(detailFrame,moved.X);Canvas.SetTop(detailFrame,moved.Y);detailFrame.Visibility=Visibility.Visible;return;}
   Point start=canvas.TranslatePoint(new Point(node.X,node.Y),fullscreenOverlay),end=canvas.TranslatePoint(new Point(node.X+KnowledgeGraphLayout.NodeWidth,node.Y+KnowledgeGraphLayout.NodeHeight),fullscreenOverlay);
   var anchor=new Rect(start,end);if(!anchor.IntersectsWith(new Rect(0,0,width,height))){detailFrame.Visibility=Visibility.Hidden;return;}
   Size card=new Size(Math.Min(420,width-24),Math.Min(640,height-24));Rect place=PlaceDetails(anchor,card,new Size(width,height));
   detailFrame.Width=place.Width;detailFrame.Height=place.Height;Canvas.SetLeft(detailFrame,place.X);Canvas.SetTop(detailFrame,place.Y);detailFrame.Visibility=Visibility.Visible;
  }
  public static Rect PlaceDetails(Rect node,Size card,Size area){
   const double gap=18,pad=12;double w=Math.Min(card.Width,Math.Max(1,area.Width-2*pad)),h=Math.Min(card.Height,Math.Max(1,area.Height-2*pad));
   double x=node.Right+gap,y=node.Top;
   if(x+w>area.Width-pad){if(node.Left-gap-w>=pad)x=node.Left-gap-w;else if(node.Bottom+gap+h<=area.Height-pad){x=node.Left;y=node.Bottom+gap;}else if(node.Top-gap-h>=pad){x=node.Left;y=node.Top-gap-h;}}
   x=Math.Max(pad,Math.Min(x,area.Width-pad-w));y=Math.Max(pad,Math.Min(y,area.Height-pad-h));return new Rect(x,y,w,h);
  }
 }
}
