using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Forms=System.Windows.Forms;

namespace ElymsPet {
 public sealed class RadialEntry {
  public PanelTab Page;public string Label,Glyph,Hint;
  public RadialEntry(PanelTab page,string label,string glyph,string hint){Page=page;Label=label;Glyph=glyph;Hint=hint;}
 }
 public static class RadialMenuLayout {
  public const string DefaultPages="Companion,Focus,Notes,Settings";
  public static readonly RadialEntry[] Entries={
   new RadialEntry(PanelTab.Companion,"陪伴","\uE8F2","说说今天的事"),
   new RadialEntry(PanelTab.Focus,"专注","\uE916","留一段专注时间"),
   new RadialEntry(PanelTab.Archive,"存档","\uE8B7","流程 · 访谈 · 云同步"),
   new RadialEntry(PanelTab.Notes,"便签","\uE70B","留住一闪而过的想法"),
   new RadialEntry(PanelTab.Workbench,"DSH","\uE756","把想法交给工作台"),
   new RadialEntry(PanelTab.Graph,"图谱","\uE8D4","领域知识 · 节点档案"),
   new RadialEntry(PanelTab.Settings,"设置","\uE713","按你的习惯调整"),
   new RadialEntry(PanelTab.Web,"联网","\uE774","搜索资料 · 逛逛 B站")};
  public static string Normalize(string value){
   if(value==null)return DefaultPages;var wanted=new HashSet<string>(value.Split(','));var names=new List<string>();
   foreach(var entry in Entries)if(wanted.Contains(entry.Page.ToString()))names.Add(entry.Page.ToString());return string.Join(",",names.ToArray());
  }
  public static bool Visible(string value,PanelTab page){return Array.IndexOf(Normalize(value).Split(','),page.ToString())>=0;}
  public static string Toggle(string value,PanelTab page,bool show){var names=new HashSet<string>(Normalize(value).Split(','));if(show)names.Add(page.ToString());else names.Remove(page.ToString());return Normalize(string.Join(",",new List<string>(names).ToArray()));}
  public static Point At(double radius,double degrees){double a=degrees*Math.PI/180;return new Point(320+radius*Math.Cos(a),320+radius*Math.Sin(a));}
  public static Geometry Slice(int index){
   double a=-90+index*45-21.9,b=a+43.8;var shape=new StreamGeometry();
   using(var c=shape.Open()){c.BeginFigure(PixelAt(126,a),true,true);c.LineTo(PixelAt(296,a),true,false);PixelArc(c,296,a,b);c.LineTo(PixelAt(126,b),true,false);PixelArc(c,126,b,a);}
   shape.Freeze();return shape;
  }
  static Point PixelAt(double radius,double angle){var p=At(radius,angle);return new Point(Math.Round(p.X/4)*4,Math.Round(p.Y/4)*4);}
  static void PixelArc(StreamGeometryContext c,double radius,double a,double b){Point prior=PixelAt(radius,a);int steps=(int)Math.Ceiling(Math.Abs(b-a)*2);for(int i=1;i<=steps;i++){var p=PixelAt(radius,a+(b-a)*i/steps);if(p==prior)continue;c.LineTo(new Point(p.X,prior.Y),true,false);c.LineTo(p,true,false);prior=p;}}
  public static Geometry PixelCircle(double radius){var g=new StreamGeometry();using(var c=g.Open()){c.BeginFigure(PixelAt(radius,0),true,true);PixelArc(c,radius,0,360);}g.Freeze();return g;}
 }
 public sealed class RadialSlice : Button {
  readonly Geometry shape;readonly RadialEntry entry;readonly Point label;readonly int index;bool shown;
  public RadialSlice(int index,RadialEntry entry,Action action){
   this.index=index;this.entry=entry;shape=RadialMenuLayout.Slice(index);label=RadialMenuLayout.At(211,-90+index*45);
   Width=Height=640;Cursor=Cursors.Hand;Background=null;BorderThickness=new Thickness(0);FocusVisualStyle=null;
   Template=new ControlTemplate(typeof(Button)){VisualTree=new FrameworkElementFactory(typeof(Grid))};
   Click+=delegate{action();};MouseEnter+=delegate{InvalidateVisual();};MouseLeave+=delegate{InvalidateVisual();};GotKeyboardFocus+=delegate{InvalidateVisual();};LostKeyboardFocus+=delegate{InvalidateVisual();};
   IsEnabledChanged+=delegate{InvalidateVisual();};PreviewMouseDown+=delegate{InvalidateVisual();};PreviewMouseUp+=delegate{InvalidateVisual();};
  }
  public void SetShown(bool value){shown=value;Content=value?entry.Label:"添加功能";ToolTip=value?entry.Hint:"添加功能 · 打开插件页";System.Windows.Automation.AutomationProperties.SetName(this,Convert.ToString(Content));InvalidateVisual();}
  protected override HitTestResult HitTestCore(PointHitTestParameters p){return shape.FillContains(p.HitPoint)?new PointHitTestResult(this,p.HitPoint):null;}
  void Text(DrawingContext dc,string text,double size,Brush brush,double x,double y,string family){
   var ft=new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(family=="pixel"?Theme.Font:new FontFamily(family),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(ft,new Point(x-ft.Width/2,y));
  }
  protected override void OnRender(DrawingContext dc){
   bool hover=IsMouseOver||IsKeyboardFocused;string fill=shown?(hover?"#DFF8F3":index%2==0?"#FFFFFF":"#EEEAFE"):(hover?"#E7EDF9":"#EEF1F7");
   dc.DrawGeometry(Theme.Brush(fill),new Pen(Theme.Brush(hover?"#008D83":"#7785A4"),2),shape);
   var ink=Theme.Brush(shown?"#334363":"#A1ACC2");
   if(shown){PixelGlyph(dc,index,ink,label.X-16,label.Y-32);Text(dc,entry.Label,24,ink,label.X,label.Y+7,"pixel");dc.DrawRectangle(Theme.Brush(index%2==0?"#73D5C6":"#D2ACF0"),null,new Rect(label.X-12,label.Y+38,24,4));}
   else {Text(dc,"+",24,ink,label.X,label.Y-15,"pixel");if(hover)Text(dc,"添加入口",12,ink,label.X,label.Y+17,"pixel");}
   if(IsKeyboardFocused)Text(dc,(index+1).ToString(),10,ink,label.X,label.Y+37,"Segoe UI");
  }
  static void PixelGlyph(DrawingContext dc,int index,Brush ink,double x,double y){string[] icons={"011111110/110000011/100000001/100101001/100000001/110000011/011111110/000110000/001100000","001111100/011000110/110000011/100010001/100011101/100000001/110000011/011000110/001111100","011111110/010000010/011111110/010000010/011111110/010000010/010000010/010000010/011111110","011111110/010000010/010111010/010000010/010111010/010000010/010110010/010000010/011111110","111111111/100000001/101000001/100100001/101000001/100011101/100000001/100000001/111111111","110000011/110000011/001000100/000101000/000111000/000101000/001000100/110000011/110000011","000111000/010111010/111000111/110010011/110111011/110010011/111000111/010111010/000111000","001111100/010010010/100010001/111111111/100010001/111111111/100010001/010010010/001111100"};var rows=icons[index].Split('/');for(int row=0;row<rows.Length;row++)for(int col=0;col<rows[row].Length;col++)if(rows[row][col]=='1')dc.DrawRectangle(ink,null,new Rect(Math.Round(x)+col*4,Math.Round(y)+row*4,4,4));}
 }
 public sealed class RadialMenuWindow : Window {
  readonly PetWindow pet;readonly Canvas wheel=new Canvas{Width=640,Height=640};readonly List<RadialSlice> slices=new List<RadialSlice>();readonly List<Button> removeButtons=new List<Button>();readonly TextBlock hint;bool shuttingDown;
  public RadialMenuWindow(PetWindow pet){
   this.pet=pet;Title="曳尔沐 · 功能圆盘";Width=Height=640;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=true;Topmost=true;FontFamily=Theme.Font;
   Content=new Viewbox{Child=wheel,Stretch=Stretch.Uniform};
   var rim=new Path{Data=RadialMenuLayout.PixelCircle(304),Fill=Theme.Brush("#FFFFFF"),Stroke=Theme.Brush("#526282"),StrokeThickness=4,IsHitTestVisible=false};wheel.Children.Add(rim);
   for(int i=0;i<RadialMenuLayout.Entries.Length;i++){
    var entry=RadialMenuLayout.Entries[i];var slice=new RadialSlice(i,entry,delegate{pet.OpenPanel(!PluginAccess.Page(pet.State,entry.Page)?PanelTab.Plugins:RadialMenuLayout.Visible(pet.State.RadialPages,entry.Page)?entry.Page:PanelTab.Plugins);});
    slice.MouseEnter+=delegate{if(hint!=null)hint.Text=!PluginAccess.Page(pet.State,entry.Page)?"安装插件后开启这个频道":RadialMenuLayout.Visible(pet.State.RadialPages,entry.Page)?entry.Hint:"点击＋添加功能";};slice.MouseLeave+=delegate{if(hint!=null)hint.Text="选择一个频道";};
    slices.Add(slice);wheel.Children.Add(slice);
    var remove=new Button{Content="−",Width=24,Height=24,FontFamily=Theme.ReadingFont,FontSize=17,Foreground=Theme.Brush("#74819B"),Background=Theme.Brush("#F7F9FD"),Cursor=Cursors.Hand,Padding=new Thickness(0),ToolTip="从圆盘移出「"+entry.Label+"」 · 保留功能和数据",Tag=entry.Page};
    remove.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='Circle' CornerRadius='12' Background='{TemplateBinding Background}' BorderBrush='#C9D2E4' BorderThickness='1'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Circle' Property='Background' Value='#FCE8EE'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Circle' Property='BorderBrush' Value='#AD506B'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Circle' Property='Background' Value='#F3CDDA'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
    System.Windows.Automation.AutomationProperties.SetName(remove,"从圆盘移出"+entry.Label);
    remove.Click+=delegate(object sender,RoutedEventArgs e){e.Handled=true;pet.State.RadialPages=RadialMenuLayout.Toggle(pet.State.RadialPages,entry.Page,false);pet.Save();RefreshEntries();hint.Text="已移出 · 点＋可重新添加";};
    var position=RadialMenuLayout.At(266,-76+i*45);Canvas.SetLeft(remove,position.X-12);Canvas.SetTop(remove,position.Y-12);Panel.SetZIndex(remove,10);removeButtons.Add(remove);wheel.Children.Add(remove);
   }
   var center=new Border{Width=240,Height=240,CornerRadius=new CornerRadius(0),Background=Theme.Brush("#FFFFFF"),BorderBrush=Theme.Brush("#8996B5"),BorderThickness=new Thickness(2)};
   var centerClip=RadialMenuLayout.PixelCircle(120).Clone();centerClip.Transform=new TranslateTransform(-200,-200);center.Clip=centerClip;
   Canvas.SetLeft(center,200);Canvas.SetTop(center,200);wheel.Children.Add(center);
   var content=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};center.Child=content;
   var face=new Border{Width=64,Height=64,Background=new ImageBrush(pet.Art.Face){Stretch=Stretch.UniformToFill},BorderBrush=Theme.Brush("#8996B5"),BorderThickness=new Thickness(2),Cursor=Cursors.SizeAll,ToolTip="拖动头像移动圆盘"};
   face.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.LeftButton==MouseButtonState.Pressed){DragMove();e.Handled=true;}};RenderOptions.SetBitmapScalingMode(face,BitmapScalingMode.NearestNeighbor);content.Children.Add(face);
   content.Children.Add(new TextBlock{Text="曳尔沐",FontSize=24,Foreground=Theme.Brush("#334363"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,7,0,2)});
   hint=new TextBlock{Text="选择一个频道",FontSize=11,Foreground=Theme.Brush("#596784"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,8)};content.Children.Add(hint);
   content.Children.Add(new TextBlock{Text="Ctrl + Alt + E  ·  Esc 收起",FontSize=9,Foreground=Theme.Brush("#596784"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,8,0,0)});
   var close=Ui.Button("×",Hide,false);close.Width=close.Height=25;close.Padding=new Thickness(0);close.Margin=new Thickness(0);close.FontSize=15;close.ToolTip="收起圆盘 · Esc";System.Windows.Automation.AutomationProperties.SetName(close,"收起圆盘");Canvas.SetLeft(close,383);Canvas.SetTop(close,230);wheel.Children.Add(close);
   PreviewKeyDown+=OnKey;
   Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!shuttingDown){e.Cancel=true;Hide();}};
   RefreshEntries();
  }
  public void RefreshEntries(){int count=0;foreach(var entry in RadialMenuLayout.Entries)if(PluginAccess.Page(pet.State,entry.Page)&&RadialMenuLayout.Visible(pet.State.RadialPages,entry.Page))count++;if(count==8){pet.State.RadialPages=RadialMenuLayout.Toggle(pet.State.RadialPages,PanelTab.Web,false);pet.Save();}for(int i=0;i<slices.Count;i++){bool shown=PluginAccess.Page(pet.State,RadialMenuLayout.Entries[i].Page)&&RadialMenuLayout.Visible(pet.State.RadialPages,RadialMenuLayout.Entries[i].Page);slices[i].SetShown(shown);removeButtons[i].Visibility=shown?Visibility.Visible:Visibility.Collapsed;}}
  public void ShowMenu(){
   RefreshEntries();Show();
   var screen=Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;var source=PresentationSource.FromVisual(this);var matrix=source==null?Matrix.Identity:source.CompositionTarget.TransformFromDevice;
   var origin=matrix.Transform(new Point(screen.Left,screen.Top));var extent=matrix.Transform(new Vector(screen.Width,screen.Height));
   double size=Math.Min(640,Math.Min(extent.X,extent.Y)-24);Width=Height=Math.Max(240,size);Left=origin.X+(extent.X-Width)/2;Top=origin.Y+(extent.Y-Height)/2;
   Activate();Focus();Opacity=1;BeginAnimation(OpacityProperty,new DoubleAnimation(.25,1,TimeSpan.FromMilliseconds(130)));
  }
  void OnKey(object sender,KeyEventArgs e){
   if(e.Key==Key.Escape){Hide();e.Handled=true;return;}
   int index=-1;if(e.Key>=Key.D1&&e.Key<=Key.D8)index=e.Key-Key.D1;else if(e.Key>=Key.NumPad1&&e.Key<=Key.NumPad8)index=e.Key-Key.NumPad1;
   if(index>=0){slices[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));e.Handled=true;return;}
   if(e.Key==Key.Left||e.Key==Key.Right||e.Key==Key.Up||e.Key==Key.Down){int at=slices.FindIndex(delegate(RadialSlice s){return s.IsKeyboardFocused;});int step=e.Key==Key.Left||e.Key==Key.Up?-1:1;slices[at<0?(step<0?7:0):(at+step+8)%8].Focus();e.Handled=true;}
  }
  public void Shutdown(){shuttingDown=true;Close();}
 }
}

