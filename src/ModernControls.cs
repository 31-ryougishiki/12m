using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Shapes;
namespace ElymsPet {
 public static class ModernControls {
  public static Button Icon(string glyph,string hint,Action action){
   var b=new Button{Content=glyph,ToolTip=hint,Width=32,Height=32,FontFamily=new FontFamily("Segoe UI Symbol"),FontSize=22,Foreground=Theme.Brush("#52617F"),Background=Theme.Brush("#F9FBFE"),BorderThickness=new Thickness(0),Cursor=System.Windows.Input.Cursors.Hand};
   b.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border x:Name='Surface' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' CornerRadius='16'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#E2EAF6'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='Background' Value='#DAF4ED'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='0.6'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
   System.Windows.Automation.AutomationProperties.SetName(b,hint);b.Click+=delegate{action();};return b;
  }
  public static Border Search(TextBox input,string placeholder){
   var surface=new Border{Background=Brushes.White,BorderBrush=Theme.Brush("#DAE1ED"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Height=44,Margin=new Thickness(0,0,0,12),Padding=new Thickness(13,0,7,0)};
   var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(30)});surface.Child=grid;
   var icon=new Path{Data=Geometry.Parse("M 12,7 A 5,5 0 1 1 2,7 A 5,5 0 1 1 12,7 M 11,11 L 16,16"),Stroke=Theme.Brush("#8291AB"),StrokeThickness=1.5,Width=18,Height=18,VerticalAlignment=VerticalAlignment.Center};grid.Children.Add(icon);
   input.ClearValue(Control.TemplateProperty);input.ClearValue(FrameworkElement.StyleProperty);input.Background=Brushes.Transparent;input.BorderThickness=new Thickness(0);input.FontFamily=Theme.ReadingFont;input.FontSize=14;input.Width=double.NaN;input.Height=double.NaN;input.Padding=new Thickness(0,2,0,2);input.Margin=new Thickness(0);input.VerticalContentAlignment=VerticalAlignment.Center;input.TextWrapping=TextWrapping.NoWrap;input.Foreground=Theme.TextMain;
   var field=new Grid();Grid.SetColumn(field,1);grid.Children.Add(field);field.Children.Add(input);
   var hint=new TextBlock{Text=placeholder,Foreground=Theme.Brush("#8793A8"),FontFamily=Theme.ReadingFont,FontSize=14,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false,TextTrimming=TextTrimming.CharacterEllipsis};field.Children.Add(hint);
   var clear=Icon("×","清除搜索",delegate{input.Clear();input.Focus();});clear.Width=clear.Height=26;clear.FontSize=18;clear.Background=Brushes.Transparent;Grid.SetColumn(clear,2);grid.Children.Add(clear);
   Action refresh=delegate{hint.Visibility=string.IsNullOrEmpty(input.Text)?Visibility.Visible:Visibility.Collapsed;clear.Visibility=string.IsNullOrEmpty(input.Text)?Visibility.Hidden:Visibility.Visible;};input.TextChanged+=delegate{refresh();};refresh();
   input.GotKeyboardFocus+=delegate{surface.BorderBrush=Theme.Brush("#62BCAF");};input.LostKeyboardFocus+=delegate{surface.BorderBrush=Theme.Brush("#DAE1ED");};return surface;
  }
  public static void Install(Application app){
   var resources=(ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='ComboBoxItem'><Setter Property='Foreground' Value='#29324B'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='14'/><Setter Property='Padding' Value='12,10'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='Row' Background='Transparent' CornerRadius='4' Margin='3,1' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='#F0F4FB'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter TargetName='Row' Property='Background' Value='#E4F6F0'/><Setter Property='Foreground' Value='#176A63'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 <Style TargetType='ComboBox'>
  <Setter Property='MinHeight' Value='44'/><Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='#29324B'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='14'/><Setter Property='BorderBrush' Value='#DAE1ED'/><Setter Property='BorderThickness' Value='1'/><Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='VerticalContentAlignment' Value='Center'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid>
   <Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6'/>
   <ContentPresenter Margin='14,0,36,0' VerticalAlignment='Center' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' ContentTemplateSelector='{TemplateBinding ItemTemplateSelector}' IsHitTestVisible='False'/>
   <ToggleButton Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Grid Background='Transparent'><Path Data='M 0,0 L 4,4 L 8,0' Stroke='#71819E' StrokeThickness='1.5' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,15,0'/></Grid></ControlTemplate></ToggleButton.Template></ToggleButton>
   <Popup x:Name='PART_Popup' AllowsTransparency='True' Focusable='False' Placement='Bottom' VerticalOffset='6' IsOpen='{TemplateBinding IsDropDownOpen}'><Border MinWidth='{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='340' Background='White' BorderBrush='#DAE1ED' BorderThickness='1' CornerRadius='8' Padding='4'><ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border></Popup>
  </Grid><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#62BCAF'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#B1BED5'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='PasswordBox'><Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='#29324B'/><Setter Property='BorderBrush' Value='#DAE1ED'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='12,8'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='PasswordBox'><Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6'><ScrollViewer x:Name='PART_ContentHost'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='#62BCAF'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
 </ResourceDictionary>");app.Resources.MergedDictionaries.Add(resources);
  }
 }
}

