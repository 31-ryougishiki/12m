using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ElymsPet {
 /// <summary>对话气泡列表。面板用宽松版，浮动输入框用紧凑版。</summary>
 public sealed class ChatView : Grid {
  readonly ScrollViewer scroller=new ScrollViewer();
  readonly StackPanel stack=new StackPanel();
  readonly StackPanel emptyPanel=new StackPanel();
  readonly TextBlock emptyText;
  readonly bool compact;
  ChatSpeaker lastSpeaker=ChatSpeaker.Notice;
  public double BubbleMaxWidth;
  public ChatView(bool compact) {
   this.compact=compact;
   BubbleMaxWidth=compact?300:440;
   scroller.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
   scroller.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
   scroller.Focusable=false;
   scroller.Padding=new Thickness(0,0,compact?2:6,0);
   scroller.Resources.Add(typeof(ScrollBar),Theme.ScrollBarStyle);
   stack.Margin=new Thickness(0,2,0,0);
   scroller.Content=stack;
   Children.Add(scroller);
   emptyPanel.HorizontalAlignment=HorizontalAlignment.Center;
   emptyPanel.Margin=new Thickness(0,compact?14:26,0,compact?14:26);
   emptyPanel.Children.Add(new TextBlock{Text="信号已接通",FontSize=compact?10.5:11.5,Foreground=Theme.Brush("#447969"),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,5)});
   emptyText=new TextBlock{Text="想说什么，直接写下来就好。",FontSize=compact?11:12,Foreground=Theme.TextDim,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=compact?280:360};
   emptyPanel.Children.Add(emptyText);
   ShowEmpty("想说什么，直接写下来就好。");
  }
  public void Clear() {
   stack.Children.Clear();lastSpeaker=ChatSpeaker.Notice;
  }
  public void ShowEmpty(string text) {
   stack.Children.Clear();lastSpeaker=ChatSpeaker.Notice;
   emptyText.Text=text??"";
   stack.Children.Add(emptyPanel);
  }
  public void Add(ChatLine line) {
   if(line==null)return;
   if(stack.Children.Count==1&&ReferenceEquals(stack.Children[0],emptyPanel))stack.Children.Clear();
   bool follow=line.Speaker!=ChatSpeaker.Notice||lastSpeaker==ChatSpeaker.User;
   UIElement element=Build(line);
   stack.Children.Add(element);
   lastSpeaker=line.Speaker;
   while(stack.Children.Count>80)stack.Children.RemoveAt(0);
   if(follow)ScrollEnd();
  }
  void ScrollEnd() {
   Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate {
    scroller.UpdateLayout();
    scroller.ScrollToEnd();
   }));
  }
  UIElement Build(ChatLine line) {
   if(line.Speaker==ChatSpeaker.Notice)return Notice(line.Text);
   bool mine=line.Speaker==ChatSpeaker.User;
   var block=new StackPanel{HorizontalAlignment=mine?HorizontalAlignment.Right:HorizontalAlignment.Left,Margin=new Thickness(0,0,0,compact?9:11)};
   var label=new TextBlock{FontSize=compact?9.5:10.5,Foreground=mine?Theme.Brush("#447969"):Theme.Lav,Margin=new Thickness(mine?0:2,mine?0:2,mine?2:0,3),HorizontalAlignment=mine?HorizontalAlignment.Right:HorizontalAlignment.Left};
   label.Inlines.Add(new Run(mine?"你":"曳尔沐"));
   var time=new Run("  "+line.Time.ToString("HH:mm")){Foreground=Theme.TextDim,FontSize=compact?8.5:9.5};
   label.Inlines.Add(time);
   block.Children.Add(label);
   var text=new TextBox{
    Text=line.Text,IsReadOnly=true,IsReadOnlyCaretVisible=false,AcceptsReturn=true,
    TextWrapping=TextWrapping.Wrap,FontFamily=Theme.Font,FontSize=compact?12.5:13.5,
    Foreground=mine?Theme.UserText:Theme.TextMain,
    Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),
    MaxWidth=BubbleMaxWidth,MinWidth=0,MinHeight=0,IsTabStop=false,Cursor=Cursors.IBeam,
    VerticalContentAlignment=VerticalAlignment.Top,FocusVisualStyle=null,
    CaretBrush=Theme.Accent,SelectionBrush=Theme.Brush("#88E5B2C5"),
    IsInactiveSelectionHighlightEnabled=true,SelectionOpacity=0.5,
    HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
    VerticalScrollBarVisibility=ScrollBarVisibility.Disabled
   };
   TextOptions.SetTextFormattingMode(text,TextFormattingMode.Display);
   text.ContextMenu=MessageMenu(text,line.Text);
   var bubble=new Border{
    Background=mine?Theme.UserBubble:Theme.HerBubble,
    BorderBrush=mine?Theme.UserBorder:Theme.HerBorder,
    BorderThickness=new Thickness(1),
    CornerRadius=mine?new CornerRadius(15,15,4,15):new CornerRadius(15,15,15,4),
    Padding=new Thickness(compact?11:14,compact?8:10,compact?12:15,compact?9:11),
    Child=text,
    HorizontalAlignment=mine?HorizontalAlignment.Right:HorizontalAlignment.Left
   };
   bubble.ContextMenu=CopyMenu(line.Text);
   block.Children.Add(bubble);
   if(!string.IsNullOrEmpty(line.Meta)) {
    block.Children.Add(new TextBlock{Text=line.Meta,FontSize=compact?8.5:9.5,Foreground=Theme.TextDim,Margin=new Thickness(mine?0:3,3,mine?3:0,0),HorizontalAlignment=mine?HorizontalAlignment.Right:HorizontalAlignment.Left});
   }
   return block;
  }
  UIElement Notice(string text) {
   bool source=(text??"").StartsWith("参考资料：",StringComparison.Ordinal);
   var border=new Border{
    Background=Theme.Brush("#F6EEE8"),
    BorderBrush=Theme.Brush("#E7D6CE"),
    BorderThickness=new Thickness(1),
    CornerRadius=new CornerRadius(10),
    Padding=new Thickness(compact?9:11,compact?5:6,compact?10:12,compact?5:6),
    Margin=new Thickness(0,0,0,compact?9:11),
    HorizontalAlignment=source?HorizontalAlignment.Stretch:HorizontalAlignment.Center,
    Child=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,TextAlignment=source?TextAlignment.Left:TextAlignment.Center,FontSize=compact?10:11,LineHeight=compact?16:18,Foreground=Theme.TextDim,MaxWidth=source?520:(compact?300:420)}
   };
   var menu=MenuTheme.Create();
   var copy=MenuTheme.Item("复制这条消息","\uE8C8",null);
   copy.Click+=delegate {TryCopy(text??"");};
   menu.Items.Add(copy);
   border.ContextMenu=menu;
   return border;
  }
  static ContextMenu CopyMenu(string text) {
   var menu=MenuTheme.Create();
   var copy=MenuTheme.Item("复制这条消息","\uE8C8",null);
   copy.Click+=delegate {TryCopy(text??"");};
   menu.Items.Add(copy);
   return menu;
  }
  internal static ContextMenu MessageMenu(TextBox box,string text) {
   var menu=MenuTheme.Create();
   var selected=MenuTheme.Item("复制选中","\uE8C8",null);
   selected.Click+=delegate {if(box.SelectionLength>0)TryCopy(box.SelectedText);};
   var all=MenuTheme.Item("复制整条","\uE8A5",null);
   all.Click+=delegate {TryCopy(text??"");};
   var selectAll=MenuTheme.Item("全选","\uE8B3",null);
   selectAll.Click+=delegate {box.Focus();box.SelectAll();};
   menu.Opened+=delegate {selected.IsEnabled=box.SelectionLength>0;};
   menu.Items.Add(selected);menu.Items.Add(all);menu.Items.Add(new Separator());menu.Items.Add(selectAll);
   return menu;
  }
  internal static bool TryCopy(string text) {
   if(string.IsNullOrEmpty(text))return false;
   for(int attempt=0;attempt<4;attempt++) {
    try {Clipboard.SetText(text);return true;}
    catch {if(attempt<3)Thread.Sleep(30);}
   }
   return false;
  }
 }

 /// <summary>渐变薄荷色的圆形发送键。</summary>
 public sealed class SendButton : Border {
  static readonly Brush Rest=Theme.Grad("#C87990","#AD506B",45);
  static readonly Brush Hover=Theme.Grad("#D48BA0","#BF6C85",45);
  readonly Action send;
  bool ready;
  public SendButton(Action action,double size) {
   send=action;
   Width=size;Height=size;CornerRadius=new CornerRadius(size/2);
   Background=Rest;BorderBrush=Theme.Brush("#D29BAE");BorderThickness=new Thickness(1);
   ToolTip="发送（Enter）";
   Child=new TextBlock{Text="↑",FontSize=size*.5,FontWeight=FontWeights.Bold,Foreground=Theme.Brush("#FFFFFF"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center};
   MouseEnter+=delegate {if(ready)Background=Hover;};
   MouseLeave+=delegate {if(ready)Background=Rest;};
   MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {
    e.Handled=true;
    if(ready&&send!=null)send();
   };
   ready=false;Opacity=.32;Cursor=Cursors.Arrow;
  }
  public bool Ready {
   get {return ready;}
   set {
    if(ready==value)return;
    ready=value;Opacity=value?1:.32;Cursor=value?Cursors.Hand:Cursors.Arrow;
    if(!value)Background=Rest;
   }
  }
 }

 /// <summary>两个窗口共用的输入框样式：透明无边框、占位提示、Enter 发送。</summary>
 public static class ChatInput {
  public static TextBox Field() {
   var box=new TextBox{
    Background=Brushes.Transparent,BorderThickness=new Thickness(0),
    Foreground=Theme.TextMain,CaretBrush=Theme.Accent,SelectionBrush=Theme.Brush("#66E5B2C5"),
    FontFamily=Theme.Font,FontSize=13,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,
    MaxLength=4000,VerticalContentAlignment=VerticalAlignment.Center,FocusVisualStyle=null
   };
   TextOptions.SetTextFormattingMode(box,TextFormattingMode.Display);
   box.ContextMenu=InputMenu(box);
   return box;
  }
  static ContextMenu InputMenu(TextBox box) {
   return MenuTheme.EditMenu(box);
  }

  public static TextBlock Placeholder(TextBox box,string text,Thickness margin) {
   var block=new TextBlock{Text=text,FontSize=12.5,Foreground=Theme.Brush("#795F69"),Margin=margin,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
   TextChangedEventHandler update=delegate {block.Visibility=box.Text.Length==0?Visibility.Visible:Visibility.Collapsed;};
   box.TextChanged+=update;
   update(null,null);
   return block;
  }
  public static void EnterSends(TextBox box,Action send) {
   box.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    if(e.Key==Key.Escape)return;
    if(e.Key==Key.ImeProcessed)return;
    if((e.Key==Key.Enter||e.Key==Key.Return)&&(Keyboard.Modifiers&ModifierKeys.Shift)==0) {
     if(send!=null)send();
     e.Handled=true;
    }
   };
  }
 }
}
