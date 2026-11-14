using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
namespace ElymsPet {
 // Keep TextBlock layout, inline code styling and bindings; add selection without editing.
 public sealed class GraphCopyText:TextBlock {
  TextPointer anchor,extent;SelectionPaint paint;AdornerLayer selectionLayer;
  public string CopyText {get{return new TextRange(ContentStart,ContentEnd).Text;}}
  public string SelectedText {get{return anchor==null||extent==null?"":new TextRange(anchor,extent).Text;}}
  public GraphCopyText(){
   Focusable=true;Cursor=Cursors.IBeam;
   var menu=new ContextMenu();var copy=new MenuItem{Header="复制选中文字",InputGestureText="Ctrl+C"};copy.Click+=delegate{CopySelection(SelectedText);};menu.Items.Add(copy);
   var all=new MenuItem{Header="复制全部文字"};all.Click+=delegate{CopySelection(CopyText);};menu.Items.Add(all);
   var select=new MenuItem{Header="全选",InputGestureText="Ctrl+A"};select.Click+=delegate{Focus();SelectAll();};menu.Items.Add(select);
   menu.Opened+=delegate{copy.IsEnabled=SelectedText.Length>0;};ContextMenu=menu;
   CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,delegate(object s,ExecutedRoutedEventArgs e){CopySelection(SelectedText);e.Handled=true;},delegate(object s,CanExecuteRoutedEventArgs e){e.CanExecute=SelectedText.Length>0;e.Handled=true;}));
   CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll,delegate(object s,ExecutedRoutedEventArgs e){SelectAll();e.Handled=true;}));
   PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Escape){ClearSelection();e.Handled=true;}};
   Unloaded+=delegate{ClearSelection();};
  }
  public void SelectAll(){anchor=ContentStart;extent=ContentEnd;Paint();}
  public void ClearSelection(){anchor=extent=null;if(paint!=null){if(selectionLayer!=null)selectionLayer.Remove(paint);paint=null;selectionLayer=null;}}
  void Begin(Point point){ClearSelection();anchor=extent=GetPositionFromPoint(point,true);Focus();Paint();}
  void Extend(Point point){extent=GetPositionFromPoint(point,true)??extent;Paint();}
  void Paint(){if(paint==null){var layer=AdornerLayer.GetAdornerLayer(this);if(layer!=null){selectionLayer=layer;paint=new SelectionPaint(this);layer.Add(paint);}}if(paint!=null)paint.InvalidateVisual();}
  public static bool Copy(string value){if(string.IsNullOrEmpty(value))return false;try{System.Windows.Forms.Clipboard.SetDataObject(value,true,5,40);return true;}catch(System.Runtime.InteropServices.ExternalException){return false;}}
  void CopySelection(string value){if(Copy(value))return;var tip=new ToolTip{Content="剪贴板暂时被占用，请重试复制",PlacementTarget=this,IsOpen=true};var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};timer.Tick+=delegate{timer.Stop();tip.IsOpen=false;};timer.Start();}
  sealed class SelectionPaint:Adorner {
   readonly GraphCopyText text;
   public SelectionPaint(GraphCopyText text):base(text){this.text=text;IsHitTestVisible=false;}
   protected override void OnRender(DrawingContext dc){
    if(text.anchor==null||text.extent==null)return;var start=text.anchor.CompareTo(text.extent)<=0?text.anchor:text.extent;var end=text.anchor.CompareTo(text.extent)<=0?text.extent:text.anchor;
    dc.PushClip(new RectangleGeometry(new Rect(text.RenderSize)));var brush=new SolidColorBrush(Color.FromArgb(65,80,120,210));
    for(var p=start;p!=null&&p.CompareTo(end)<0;){var next=p.GetNextInsertionPosition(LogicalDirection.Forward);if(next==null)break;var a=p.GetCharacterRect(LogicalDirection.Forward);var b=next.GetCharacterRect(LogicalDirection.Backward);if(!a.IsEmpty&&!b.IsEmpty&&Math.Abs(a.Top-b.Top)<2)dc.DrawRectangle(brush,null,new Rect(Math.Min(a.Left,b.Left),a.Top,Math.Max(1,Math.Abs(b.Left-a.Left)),Math.Max(a.Height,b.Height)));p=next;}
    dc.Pop();
   }
  }
  static DependencyObject Parent(DependencyObject value){var content=value as FrameworkContentElement;return content!=null?content.Parent:value is Visual?VisualTreeHelper.GetParent(value):LogicalTreeHelper.GetParent(value);}
  static T Ancestor<T>(DependencyObject value)where T:DependencyObject{for(var p=value;p!=null;p=Parent(p))if(p is T)return (T)p;return null;}
  public static void Enable(UIElement root){
   GraphCopyText pending=null,previous=null;Point down=new Point();bool dragging=false,interactive=false;
   root.PreviewMouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){
    pending=Ancestor<GraphCopyText>(e.OriginalSource as DependencyObject);dragging=false;if(previous!=null&&previous!=pending)previous.ClearSelection();previous=pending;if(pending==null)return;
    down=e.GetPosition(root);interactive=Ancestor<ButtonBase>(pending)!=null||Ancestor<Hyperlink>(e.OriginalSource as DependencyObject)!=null;
    if(!interactive){pending.Begin(e.GetPosition(pending));e.Handled=true;}
   };
   root.PreviewMouseMove+=delegate(object s,MouseEventArgs e){if(pending==null||e.LeftButton!=MouseButtonState.Pressed)return;var p=e.GetPosition(root);if(!dragging&&(Math.Abs(p.X-down.X)>SystemParameters.MinimumHorizontalDragDistance||Math.Abs(p.Y-down.Y)>SystemParameters.MinimumVerticalDragDistance)){dragging=true;if(interactive)pending.Begin(root.TranslatePoint(down,pending));root.CaptureMouse();}if(dragging){pending.Extend(e.GetPosition(pending));e.Handled=true;}};
   root.PreviewMouseLeftButtonUp+=delegate(object s,MouseButtonEventArgs e){if(dragging){e.Handled=true;root.ReleaseMouseCapture();}pending=null;dragging=false;};
   // Template-generated captions (tabs, expanders, buttons) can also be copied.
   root.PreviewMouseRightButtonDown+=delegate(object s,MouseButtonEventArgs e){var source=e.OriginalSource as DependencyObject;if(Ancestor<TextBox>(source)!=null||Ancestor<GraphCopyText>(source)!=null)return;var text=Ancestor<TextBlock>(source);if(text==null)return;var menu=new ContextMenu{PlacementTarget=text};var copy=new MenuItem{Header="复制文字"};copy.Click+=delegate{Copy(new TextRange(text.ContentStart,text.ContentEnd).Text);};menu.Items.Add(copy);menu.IsOpen=true;e.Handled=true;};
  }
 }
}
