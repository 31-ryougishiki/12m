using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace ElymsPet {
 /// <summary>Warm, keyboard-accessible popup menus shared by desktop actions.</summary>
 public static class MenuTheme {
  static ResourceDictionary resources;
  static ResourceDictionary Resources {
   get {
    if(resources==null)resources=(ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style TargetType='Separator'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'><Border Height='1' Background='#E9D6CF' Margin='12,6'/></ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator' BasedOn='{StaticResource {x:Type Separator}}'/>
 <Style TargetType='MenuItem'>
  <Setter Property='Foreground' Value='#29324B'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='FontSize' Value='13'/>
  <Setter Property='Padding' Value='10,9'/>
  <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
  <Setter Property='Template'>
   <Setter.Value>
    <ControlTemplate TargetType='MenuItem'>
     <Grid>
      <Border x:Name='Row' Background='{TemplateBinding Background}' CornerRadius='0' Margin='0,1' Padding='{TemplateBinding Padding}'>
       <Grid>
        <Grid.ColumnDefinitions><ColumnDefinition Width='27'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/><ColumnDefinition Width='16'/></Grid.ColumnDefinitions>
        <ContentPresenter x:Name='Icon' ContentSource='Icon' VerticalAlignment='Center'/>
        <Border x:Name='CheckBox' Width='16' Height='16' HorizontalAlignment='Left' VerticalAlignment='Center' CornerRadius='0' BorderBrush='#C18A9A' BorderThickness='1' Visibility='Collapsed'>
         <Path x:Name='Tick' Data='M 3,7 L 6,10 L 12,4' Stroke='White' StrokeThickness='1.6' Visibility='Collapsed'/>
        </Border>
        <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
        <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Foreground='#59647E' FontSize='10.5' Margin='20,0,0,0' VerticalAlignment='Center'/>
        <Path x:Name='Arrow' Grid.Column='3' Data='M 0,0 L 4,4 L 0,8' Stroke='#987382' StrokeThickness='1.3' HorizontalAlignment='Right' VerticalAlignment='Center' Visibility='Collapsed'/>
       </Grid>
      </Border>
      <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='3' IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
       <Border Background='#FFFFFF' BorderBrush='#8996B5' BorderThickness='1' CornerRadius='0' Padding='7' MinWidth='232'>
        <ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' MaxHeight='480'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' KeyboardNavigation.TabNavigation='Cycle'/></ScrollViewer>
       </Border>
      </Popup>
     </Grid>
     <ControlTemplate.Triggers>
      <Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
      <Trigger Property='IsCheckable' Value='True'><Setter TargetName='Icon' Property='Visibility' Value='Collapsed'/><Setter TargetName='CheckBox' Property='Visibility' Value='Visible'/></Trigger>
      <Trigger Property='IsChecked' Value='True'><Setter TargetName='CheckBox' Property='Background' Value='#5368B2'/><Setter TargetName='Tick' Property='Visibility' Value='Visible'/></Trigger>
      <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Row' Property='Background' Value='#E5E0FF'/><Setter Property='Foreground' Value='#33497E'/></Trigger>
      <Trigger Property='IsSubmenuOpen' Value='True'><Setter TargetName='Row' Property='Background' Value='#E5E0FF'/><Setter Property='Foreground' Value='#33497E'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Row' Property='Opacity' Value='0.45'/></Trigger>
     </ControlTemplate.Triggers>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
 </Style>
 <Style TargetType='ContextMenu'>
  <Setter Property='OverridesDefaultStyle' Value='True'/>
  <Setter Property='MinWidth' Value='270'/>
  <Setter Property='Background' Value='#FFFFFF'/>
  <Setter Property='Template'>
   <Setter.Value>
    <ControlTemplate TargetType='ContextMenu'>
     <Border Background='{TemplateBinding Background}' BorderBrush='#8996B5' BorderThickness='1' CornerRadius='0' Padding='7' SnapsToDevicePixels='True'>
      <DockPanel>
       <Border x:Name='Brand' DockPanel.Dock='Top' Background='#E7EDF9' CornerRadius='0' Padding='12,10' Margin='0,0,0,6'>
        <StackPanel><TextBlock Text='曳尔沐  ▪ ▪ ▪' Foreground='#33497E' FontSize='14' FontWeight='SemiBold'/><TextBlock Text='把一点温柔，留在你的桌面。' Foreground='#59647E' FontSize='10.5' Margin='0,4,0,0'/></StackPanel>
       </Border>
       <ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' MaxHeight='480'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' KeyboardNavigation.TabNavigation='Cycle'/></ScrollViewer>
      </DockPanel>
     </Border>
     <ControlTemplate.Triggers><Trigger Property='Tag' Value='compact'><Setter TargetName='Brand' Property='Visibility' Value='Collapsed'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
 </Style>
</ResourceDictionary>");
    return resources;
   }
  }
  public static ContextMenu Create(bool branded=false) {
   var menu=new ContextMenu{FontFamily=Theme.Font,UseLayoutRounding=true,SnapsToDevicePixels=true,Tag=branded?"brand":"compact",MinWidth=branded?270:220};
   menu.Resources.MergedDictionaries.Add(Resources);
   menu.Style=(Style)Resources[typeof(ContextMenu)];
   menu.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   return menu;
  }
  public static ContextMenu EditMenu(System.Windows.Controls.Primitives.TextBoxBase box) {
   var menu=Create();
   var text=box as TextBox;
   bool readOnly=text!=null&&text.IsReadOnly;
   if(!readOnly)menu.Items.Add(Command("剪切","\uE8C6",System.Windows.Input.ApplicationCommands.Cut,box,"Ctrl + X"));
   menu.Items.Add(Command("复制","\uE8C8",System.Windows.Input.ApplicationCommands.Copy,box,"Ctrl + C"));
   if(!readOnly)menu.Items.Add(Command("粘贴","\uE77F",System.Windows.Input.ApplicationCommands.Paste,box,"Ctrl + V"));
   menu.Items.Add(new Separator());
   menu.Items.Add(Command("全选","\uE8B3",System.Windows.Input.ApplicationCommands.SelectAll,box,"Ctrl + A"));
   return menu;
  }
  public static MenuItem Command(string title,string glyph,System.Windows.Input.ICommand command,IInputElement target,string shortcut) {
   var item=Item(title,glyph,null);item.Command=command;item.CommandTarget=target;item.InputGestureText=shortcut;return item;
  }
  public static void Install(Application app) {
   app.Resources.MergedDictionaries.Add(Resources);ModernControls.Install(app);
   app.Resources.Add(typeof(ToolTip),(Style)XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:sys='clr-namespace:System;assembly=mscorlib' TargetType='ToolTip'>
    <Setter Property='Foreground' Value='#29324B'/><Setter Property='FontFamily' Value='Microsoft YaHei UI'/><Setter Property='FontSize' Value='12'/><Setter Property='MaxWidth' Value='360'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'><Border Background='#FFFFFF' BorderBrush='#8996B5' BorderThickness='1' CornerRadius='0' Padding='12,8'><ContentPresenter><ContentPresenter.Resources><DataTemplate DataType='{x:Type sys:String}'><TextBlock Text='{Binding}' TextWrapping='Wrap'/></DataTemplate></ContentPresenter.Resources></ContentPresenter></Border></ControlTemplate></Setter.Value></Setter>
   </Style>"));
   EventManager.RegisterClassHandler(typeof(TextBox),FrameworkElement.LoadedEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs args) {
    var box=(TextBox)sender;if(box.ContextMenu==null)box.ContextMenu=EditMenu(box);
   }));
   EventManager.RegisterClassHandler(typeof(PasswordBox),FrameworkElement.LoadedEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs args) {
    var box=(PasswordBox)sender;if(box.ContextMenu!=null)return;
    var menu=Create();menu.Items.Add(Command("粘贴","\uE77F",System.Windows.Input.ApplicationCommands.Paste,box,"Ctrl + V"));
    menu.Items.Add(Command("全选","\uE8B3",System.Windows.Input.ApplicationCommands.SelectAll,box,"Ctrl + A"));box.ContextMenu=menu;
   }));
  }
  public static MenuItem Item(string title,string glyph,Action action) {
   var item=new MenuItem{Header=title,Icon=new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=15,VerticalAlignment=VerticalAlignment.Center}};
   if(action!=null)item.Click+=delegate {action();};
   return item;
  }
 }
}


