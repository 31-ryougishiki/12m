using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

namespace ElymsPet {
 public static class PopupDialog {
  public static MessageBoxResult Show(string text,string title) {return Show(null,text,title,MessageBoxButton.OK,MessageBoxImage.Information);}
  public static MessageBoxResult Show(Window owner,string text,string title) {return Show(owner,text,title,MessageBoxButton.OK,MessageBoxImage.Information);}
  public static MessageBoxResult Show(string text,string title,MessageBoxButton buttons,MessageBoxImage icon) {return Show(null,text,title,buttons,icon);}
  public static MessageBoxResult Show(Window owner,string text,string title,MessageBoxButton buttons,MessageBoxImage icon) {
   var dialog=new PromptWindow(text,title,buttons,icon);
   if(owner!=null&&owner.IsVisible) {dialog.Owner=owner;dialog.Topmost=owner.Topmost;dialog.WindowStartupLocation=owner is PetWindow?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;}
   dialog.ShowDialog();return dialog.Outcome;
  }
 }
 internal sealed class PromptWindow : Window {
  public MessageBoxResult Outcome {get;private set;}
  public PromptWindow(string text,string title,MessageBoxButton buttons,MessageBoxImage icon) {
   Outcome=DismissResult(buttons);
   Title=title;Width=Math.Min(480,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;
   WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;
   ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=Theme.Font;UseLayoutRounding=true;
   var body=new DockPanel{Margin=new Thickness(22)};
   Content=new Border{Background=Theme.Surface,BorderBrush=Theme.Brush("#DEC0C5"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Child=body};
   var head=new DockPanel{Margin=new Thickness(0,0,0,18)};DockPanel.SetDock(head,Dock.Top);body.Children.Add(head);
   var close=Ui.Button("×",delegate {Close();},false);close.Width=32;close.Height=32;close.Padding=new Thickness(0);close.Margin=new Thickness(12,0,0,0);close.ToolTip="关闭";System.Windows.Automation.AutomationProperties.SetName(close,"关闭对话框");DockPanel.SetDock(close,Dock.Right);head.Children.Add(close);
   var titleText=new TextBlock{Text=title,FontSize=18,FontWeight=FontWeights.SemiBold,Foreground=Theme.TextMain,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};head.Children.Add(titleText);
   head.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {if(e.ButtonState==MouseButtonState.Pressed)DragMove();};
   var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);
   if(buttons==MessageBoxButton.YesNo||buttons==MessageBoxButton.YesNoCancel) {
    actions.Children.Add(Choice(buttons==MessageBoxButton.YesNo?"取消":"否",MessageBoxResult.No,false,true));
    if(buttons==MessageBoxButton.YesNoCancel)actions.Children.Add(Choice("取消",MessageBoxResult.Cancel,false,false));
    actions.Children.Add(Choice("确认",MessageBoxResult.Yes,true,false));
   } else {
    if(buttons==MessageBoxButton.OKCancel)actions.Children.Add(Choice("取消",MessageBoxResult.Cancel,false,true));
    actions.Children.Add(Choice("确定",MessageBoxResult.OK,true,buttons==MessageBoxButton.OK));
   }
   var message=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Foreground=Theme.TextMain,FontSize=13,Padding=new Thickness(0),IsTabStop=false,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=Math.Max(100,SystemParameters.WorkArea.Height-220)};
   message.ContextMenu=MenuTheme.EditMenu(message);
   var details=new StackPanel();
   string hint=icon==MessageBoxImage.Warning?"请确认后再继续":icon==MessageBoxImage.Question?"需要你的确认":icon==MessageBoxImage.Error?"遇到了一点问题":"温馨提示";
   details.Children.Add(new TextBlock{Text=hint,Foreground=Theme.Accent,FontSize=11,Margin=new Thickness(0,0,0,10)});details.Children.Add(message);body.Children.Add(details);
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape){e.Handled=true;Close();}};
  }
  internal static MessageBoxResult DismissResult(MessageBoxButton buttons) {return buttons==MessageBoxButton.YesNo?MessageBoxResult.No:buttons==MessageBoxButton.OK?MessageBoxResult.OK:MessageBoxResult.Cancel;}
  Button Choice(string label,MessageBoxResult result,bool primary,bool isDefault) {
   var button=Ui.Button(label,delegate {Outcome=result;Close();},primary);button.MinWidth=80;button.Margin=new Thickness(8,0,0,0);button.IsDefault=isDefault;
   if(isDefault)Loaded+=delegate {button.Focus();};return button;
  }
 }
 public sealed class WarmTrayRenderer : Forms.ToolStripProfessionalRenderer {
  public WarmTrayRenderer():base(new WarmTrayColors()) {RoundedEdges=true;}
  protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e) {
   e.TextColor=Drawing.Color.FromArgb(89,67,75);base.OnRenderItemText(e);
  }
 }
 public sealed class WarmTrayColors : Forms.ProfessionalColorTable {
  public WarmTrayColors(){UseSystemColors=false;}
  static Drawing.Color Surface {get{return Drawing.Color.FromArgb(255,252,249);}}
  static Drawing.Color Selected {get{return Drawing.Color.FromArgb(244,220,228);}}
  public override Drawing.Color ToolStripDropDownBackground {get{return Surface;}}
  public override Drawing.Color MenuItemSelected {get{return Selected;}}
  public override Drawing.Color MenuItemSelectedGradientBegin {get{return Selected;}}
  public override Drawing.Color MenuItemSelectedGradientEnd {get{return Selected;}}
  public override Drawing.Color MenuItemBorder {get{return Drawing.Color.FromArgb(197,140,160);}}
  public override Drawing.Color MenuBorder {get{return Drawing.Color.FromArgb(222,192,197);}}
  public override Drawing.Color SeparatorDark {get{return Drawing.Color.FromArgb(233,214,207);}}
  public override Drawing.Color SeparatorLight {get{return Surface;}}
 }
}
