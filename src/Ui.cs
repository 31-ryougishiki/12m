using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace ElymsPet {
 /// <summary>界面配色与字体，陪伴面板、浮动输入框和桌宠共用。</summary>
 public static class Theme {
  public static readonly FontFamily Font=new FontFamily("Microsoft YaHei UI");
  public static readonly SolidColorBrush TextMain=Brush("#ECF2FF");
  public static readonly SolidColorBrush TextSoft=Brush("#A9BBD8");
  public static readonly SolidColorBrush TextDim=Brush("#8498BA");
  public static readonly SolidColorBrush Mint=Brush("#AFEDE1");
  public static readonly SolidColorBrush MintDeep=Brush("#7BE3D1");
  public static readonly SolidColorBrush Lav=Brush("#B7C3FA");
  public static readonly SolidColorBrush HerBubble=Brush("#2AA9BCFA");
  public static readonly SolidColorBrush HerBorder=Brush("#4AA9BCFA");
  public static readonly SolidColorBrush UserBubble=Brush("#33A9E8D8");
  public static readonly SolidColorBrush UserBorder=Brush("#55A9E8D8");
  public static readonly SolidColorBrush UserText=Brush("#EAFBF7");
  public static readonly SolidColorBrush Line=Brush("#22304B");
  static Style scrollBarStyle;
  /// <summary>深色细滚动条：面板和对话气泡共用，避免默认白条打断深色界面。</summary>
  public static Style ScrollBarStyle {
   get {
    if(scrollBarStyle==null)scrollBarStyle=BuildScrollBarStyle();
    return scrollBarStyle;
   }
  }
  static Style BuildScrollBarStyle() {
   var style=new Style(typeof(ScrollBar));
   style.Setters.Add(new Setter(FrameworkElement.WidthProperty,7.0));
   style.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
   style.Setters.Add(new Setter(Control.TemplateProperty,BuildScrollTemplate()));
   return style;
  }
  static ControlTemplate BuildScrollTemplate() {
   string xaml=@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
 <Grid Background='Transparent'>
  <Track x:Name='PART_Track' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
   <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
   <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border Background='#4DFFFFFF' CornerRadius='3' Margin='1,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
   <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
  </Track>
 </Grid>
</ControlTemplate>";
   return (ControlTemplate)XamlReader.Parse(xaml);
  }
  public static SolidColorBrush Brush(string hex) {
   var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
   brush.Freeze();return brush;
  }
  public static LinearGradientBrush Grad(string from,string to,double angle) {
   var brush=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(from),(Color)ColorConverter.ConvertFromString(to),angle);
   brush.Freeze();return brush;
  }
  public static LinearGradientBrush Grad3(string from,string middle,string to) {
   var brush=new LinearGradientBrush();
   brush.StartPoint=new Point(0,.5);brush.EndPoint=new Point(1,.5);
   brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(from),0));
   brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(middle),.5));
   brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(to),1));
   brush.Freeze();return brush;
  }
 }

 /// <summary>WPF 控件工厂与后台任务辅助：窗口代码只描述布局，不重复样板。</summary>
 public static class Ui {
  public static SolidColorBrush Brush(string hex) {
   var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);
   brush.Freeze();return brush;
  }
  public static TextBlock Text(string text,double size,string color) {
   return new TextBlock{Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
  }
  public static Border Card(UIElement child) {
   return new Border{Background=Brush("#19263D"),BorderBrush=Brush("#2B3B58"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(20),Margin=new Thickness(0,0,0,14),Child=child};
  }
  public static TextBox Input(string text,int height) {
   return new TextBox{Text=text,Height=height,Background=Brush("#111D31"),Foreground=Brush("#E6EDFF"),CaretBrush=Brush("#AFEFDF"),BorderBrush=Brush("#3B4C6A"),Padding=new Thickness(12,9,12,9),FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),VerticalContentAlignment=VerticalAlignment.Center};
  }
  public static Button Button(string label,Action action,bool primary) {
   var button=new Button{Content=label,Padding=new Thickness(15,10,15,10),Margin=new Thickness(0,0,9,9),Background=Brush(primary?"#AFEDE1":"#293955"),Foreground=Brush(primary?"#132A31":"#E0E7FA"),BorderThickness=new Thickness(0),Cursor=System.Windows.Input.Cursors.Hand,FontSize=13};
   var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
   var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.MarginProperty,new TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);border.AppendChild(presenter);button.Template=new ControlTemplate(typeof(Button)){VisualTree=border};button.Click+=delegate {action();};return button;
  }
  public static CheckBox Check(string label,bool value,Action<bool> action) {
   var box=new CheckBox{Content=label,IsChecked=value,Foreground=Brush("#D3DEF2"),Margin=new Thickness(0,0,0,20)};
   box.Click+=delegate {action(box.IsChecked==true);};
   return box;
  }
  public static WrapPanel Row(params UIElement[] items) {
   var panel=new WrapPanel();foreach(var item in items)panel.Children.Add(item);return panel;
  }
  public static StackPanel Stack(params UIElement[] items) {
   var panel=new StackPanel();foreach(var item in items)panel.Children.Add(item);return panel;
  }
  /// <summary>把耗时工作放到线程池，完成后再回到 UI 线程；窗口已关闭时安静丢弃结果。</summary>
  public static void InBackground<T>(Dispatcher dispatcher,Func<T> work,Action<T> done) {
   ThreadPool.QueueUserWorkItem(delegate {
    T result=work();
    try {dispatcher.BeginInvoke(new Action(delegate {done(result);}));} catch {}
   });
  }
 }

 /// <summary>界面上的时间文本：计时用 mm:ss，存档时间用 MM-dd HH:mm。</summary>
 public static class TimeText {
  public static string Short(double seconds) {
   int total=Math.Max(0,(int)Math.Ceiling(seconds));
   return (total/60).ToString("00")+":"+(total%60).ToString("00");
  }
  public static string Local(DateTime value) {
   if(value==DateTime.MinValue) return "—";
   var utc=value.Kind==DateTimeKind.Utc?value:new DateTime(value.Ticks,DateTimeKind.Utc);
   return utc.ToLocalTime().ToString("MM-dd HH:mm");
  }
 }
}
