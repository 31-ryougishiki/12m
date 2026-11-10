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
  // Cream stationery, cherry blossom accents, and cocoa text.
  public static readonly SolidColorBrush Canvas=Brush("#FFF7F0");
  public static readonly SolidColorBrush Surface=Brush("#FFFCF9");
  public static readonly SolidColorBrush Sidebar=Brush("#F8E7DE");
  public static readonly SolidColorBrush Accent=Brush("#AD506B");
  public static readonly FontFamily Font=new FontFamily("Microsoft YaHei UI");
  public static readonly SolidColorBrush TextMain=Brush("#4F3B43");
  public static readonly SolidColorBrush TextSoft=Brush("#78616A");
  public static readonly SolidColorBrush TextDim=Brush("#795F69");
  public static readonly SolidColorBrush Success=Brush("#447969");
  public static readonly SolidColorBrush Lav=Brush("#78669B");
  public static readonly SolidColorBrush HerBubble=Brush("#F2EAF7");
  public static readonly SolidColorBrush HerBorder=Brush("#DCCCE6");
  public static readonly SolidColorBrush UserBubble=Brush("#F5E1D5");
  public static readonly SolidColorBrush UserBorder=Brush("#E6C6B5");
  public static readonly SolidColorBrush UserText=Brush("#59434B");
  public static readonly SolidColorBrush Line=Brush("#E9D6CF");
  static Style scrollBarStyle;
  /// <summary>柔和的玫瑰色滚动条，面板和对话气泡共用。</summary>
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
   <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border Background='#C9A9AB' CornerRadius='3' Margin='1,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
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

 /// <summary>WPF 控件工厂：窗口代码只描述布局，不重复样板。</summary>
 public static class Ui {
  public static SolidColorBrush Brush(string hex) {
   var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);
   brush.Freeze();return brush;
  }
  public static TextBlock Text(string text,double size,string color) {
   return new TextBlock{Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
  }
  public static Border Card(UIElement child) {
   return new Border{Background=Theme.Surface,BorderBrush=Theme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(20),Margin=new Thickness(0,0,0,14),Child=child};
  }
  static ControlTemplate buttonTemplate,inputTemplate,checkTemplate;
  public static TextBox Input(string text,int height) {
   var field=new TextBox{Text=text,Height=height,Background=Brush("#FFFCF9"),Foreground=Theme.TextMain,CaretBrush=Theme.Accent,BorderBrush=Brush("#D6B7AE"),BorderThickness=new Thickness(1),Padding=new Thickness(12,9,12,9),FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),VerticalContentAlignment=VerticalAlignment.Center};
   if(inputTemplate==null)inputTemplate=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'>
    <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
     <ScrollViewer x:Name='PART_ContentHost'/>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#AD506B'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate>");
   field.Template=inputTemplate;return field;
  }
  public static Button Button(string label,Action action,bool primary) {
   var button=new Button{Content=label,Padding=new Thickness(15,10,15,10),Margin=new Thickness(0,0,9,9),Background=Brush(primary?"#AD506B":"#F6EAE3"),Foreground=Brush(primary?"#FFFFFF":"#624B53"),BorderBrush=Brush(primary?"#AD506B":"#DEC3BA"),BorderThickness=new Thickness(1),Cursor=System.Windows.Input.Cursors.Hand,FontSize=13,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};
   if(buttonTemplate==null)buttonTemplate=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
    <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9'>
     <Grid><Border x:Name='Wash' Background='#873F59' Opacity='0' CornerRadius='8'/>
      <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/>
     </Grid>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Wash' Property='Opacity' Value='0.08'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#873F59'/><Setter TargetName='Wash' Property='Opacity' Value='0.12'/></Trigger>
     <Trigger Property='IsPressed' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='0.72'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.5'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate>");
   button.Template=buttonTemplate;button.Click+=delegate {action();};return button;
  }
  public static CheckBox Check(string label,bool value,Action<bool> action) {
   var box=new CheckBox{Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap},IsChecked=value,Foreground=Brush("#59434B"),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(7,1,0,1),Margin=new Thickness(0,0,0,18)};
   if(checkTemplate==null)checkTemplate=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
    <Grid x:Name='Root' Background='Transparent'>
     <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
     <Border x:Name='Box' Width='17' Height='17' Margin='0,2,0,0' VerticalAlignment='Top' CornerRadius='5' Background='#FFFCF9' BorderBrush='#AD7D8C' BorderThickness='1'>
      <Path x:Name='Tick' Data='M 3,7 L 6,10 L 12,4' Stroke='White' StrokeThickness='1.8' Visibility='Collapsed'/>
     </Border>
     <ContentPresenter Grid.Column='1' Margin='{TemplateBinding Padding}' RecognizesAccessKey='True'/>
    </Grid>
    <ControlTemplate.Triggers>
     <Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='#AD506B'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Box' Property='BorderBrush' Value='#873F59'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderThickness' Value='2'/><Setter TargetName='Box' Property='BorderBrush' Value='#873F59'/></Trigger>
     <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Root' Property='Opacity' Value='0.5'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate>");
   box.Template=checkTemplate;
   System.Windows.Automation.AutomationProperties.SetName(box,label);
   box.Click+=delegate {action(box.IsChecked==true);};return box;
  }
  public static WrapPanel Row(params UIElement[] items) {
   var panel=new WrapPanel();foreach(var item in items)panel.Children.Add(item);return panel;
  }
  public static StackPanel Stack(params UIElement[] items) {
   var panel=new StackPanel();foreach(var item in items)panel.Children.Add(item);return panel;
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
