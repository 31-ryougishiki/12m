using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ElymsPet {
 /// <summary>透明的浮动输入框：始终置顶，做别的事情时也能随手和曳尔沐说一句话。</summary>
 public sealed class ChatBarWindow : Window {
  const string GlyphClose="M2,2 L9,9 M9,2 L2,9";
  const string GlyphCollapse="M1,7 L5.5,2.5 L10,7";
  const string GlyphExpand="M1,3.5 L5.5,8 L10,3.5";
  const string GlyphPanel="M1.5,2 L9.5,2 L9.5,10 L1.5,10 Z M1.5,5 L9.5,5 M5.5,5 L5.5,10";
  readonly PetWindow pet;
  readonly ChatService chat;
  readonly ArchiveService archive;
  readonly TextBox input;
  readonly TextBlock headerStatus,metaText,inputPlaceholder,archiveSlotText,chatLabel,archiveLabel,hintText,questionText;
  readonly ChatView view,archiveView;
  readonly SendButton send;
  readonly Path historyIcon;
  readonly Border historyButton,chatPill,archivePill,questionBanner;
  bool showHistory,placed,userMoved,archiveMode;
  double bottomAnchor=double.NaN;
  public ChatBarWindow(PetWindow target) {
   pet=target;chat=target.Conversation;archive=target.Archive;showHistory=pet.State.ChatBarHistory;
   Title="曳尔沐 · 浮动对话";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
   AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=true;
   Topmost=true;Width=486;SizeToContent=SizeToContent.Height;FontFamily=Theme.Font;
   UseLayoutRounding=true;SnapsToDevicePixels=true;
   TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
   var root=new Grid();
   root.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(14,0,0,0)),CornerRadius=new CornerRadius(30),Margin=new Thickness(8)});
   root.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(30,0,0,0)),CornerRadius=new CornerRadius(26),Margin=new Thickness(13)});
   var card=new Border{
    Background=Theme.Grad("#FFFFFCF9","#FFFFF0E8",90),
    BorderBrush=Theme.Brush("#D9BDB6"),BorderThickness=new Thickness(1),
    CornerRadius=new CornerRadius(22),Margin=new Thickness(18)
   };
   root.Children.Add(card);
   var body=new Grid();
   for(int i=0;i<7;i++)body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   card.Child=body;
   var header=new Grid{Margin=new Thickness(16,14,12,12)};
   header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   header.ColumnDefinitions.Add(new ColumnDefinition());
   header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var face=new Grid{Width=38,Height=38,Margin=new Thickness(0,0,11,0),VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.SizeAll};
   var avatar=new Border{CornerRadius=new CornerRadius(19),Background=new ImageBrush(pet.Art.Face){Stretch=Stretch.UniformToFill},BorderBrush=Theme.Brush("#D5B9D6"),BorderThickness=new Thickness(1)};
   RenderOptions.SetBitmapScalingMode(avatar,BitmapScalingMode.HighQuality);
   face.Children.Add(avatar);
   var dot=new Ellipse{Width=9,Height=9,Fill=Theme.Success,Stroke=Theme.Brush("#FFFCF9"),StrokeThickness=1.6,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,1,1)};
   face.Children.Add(dot);
   header.Children.Add(face);Grid.SetColumn(face,0);
   var names=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.SizeAll,Background=Brushes.Transparent};
   var titleRow=new StackPanel{Orientation=Orientation.Horizontal};
   titleRow.Children.Add(new TextBlock{Text="曳尔沐",FontSize=13.5,FontWeight=FontWeights.SemiBold,Foreground=Theme.TextMain});
   titleRow.Children.Add(new TextBlock{Text="ELYMS",FontSize=8.5,Foreground=Theme.Lav,Margin=new Thickness(7,2,0,0),VerticalAlignment=VerticalAlignment.Center});
   names.Children.Add(titleRow);
   headerStatus=new TextBlock{Text="● 已接入 DeepSeek",FontSize=9.5,Foreground=Theme.Brush("#447969"),Margin=new Thickness(0,3,0,0)};
   names.Children.Add(headerStatus);
   header.Children.Add(names);Grid.SetColumn(names,1);
   var tools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Right};
   historyIcon=GlyphPath(showHistory?GlyphCollapse:GlyphExpand);
   if(!showHistory)historyIcon.Stroke=Theme.Brush("#795F69");
   historyButton=IconButton(historyIcon,showHistory?"收起对话记录":"展开对话记录",delegate {ToggleHistory();});
   tools.Children.Add(historyButton);
   tools.Children.Add(IconButton(GlyphPath(GlyphPanel),"打开陪伴面板",delegate {pet.OpenPanel(archiveMode?PanelTab.Archive:PanelTab.Companion);}));
   tools.Children.Add(IconButton(GlyphPath(GlyphClose),"收起（Esc）",delegate {HideBar();}));
   header.Children.Add(tools);Grid.SetColumn(tools,2);
   body.Children.Add(header);Grid.SetRow(header,0);
   var divider=new Border{Height=1,Margin=new Thickness(16,0,16,0),Background=Theme.Grad3("#00FFFFFF","#E4CCC6","#00FFFFFF")};
   body.Children.Add(divider);Grid.SetRow(divider,1);
   var toolbar=new Grid{Margin=new Thickness(16,6,12,9)};
   toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   toolbar.ColumnDefinitions.Add(new ColumnDefinition());
   var modes=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
   chatPill=ModePill("对话",delegate {SetMode(false);},out chatLabel);
   archivePill=ModePill("存档",delegate {SetMode(true);},out archiveLabel);
   modes.Children.Add(chatPill);modes.Children.Add(archivePill);
   toolbar.Children.Add(modes);Grid.SetColumn(modes,0);
   archiveSlotText=new TextBlock{
    Text="",FontSize=10,Foreground=Theme.TextSoft,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,
    Cursor=Cursors.Hand,MaxWidth=210,TextTrimming=TextTrimming.CharacterEllipsis,Visibility=Visibility.Collapsed,
    ToolTip="切换存档 / 新建存档"
   };
   archiveSlotText.MouseEnter+=delegate {archiveSlotText.Foreground=Theme.Accent;};
   archiveSlotText.MouseLeave+=delegate {archiveSlotText.Foreground=Theme.TextSoft;};
   archiveSlotText.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {e.Handled=true;OpenSlotMenu();};
   toolbar.Children.Add(archiveSlotText);Grid.SetColumn(archiveSlotText,1);
   body.Children.Add(toolbar);Grid.SetRow(toolbar,2);
   questionText=new TextBlock{FontSize=11.5,LineHeight=18,Foreground=Theme.TextSoft,TextWrapping=TextWrapping.Wrap};
   questionBanner=new Border{
    Margin=new Thickness(14,0,14,8),Padding=new Thickness(11,7,11,8),CornerRadius=new CornerRadius(11),
    Background=Theme.Brush("#F6EEE8"),BorderBrush=Theme.Brush("#E7D6CE"),BorderThickness=new Thickness(1),
    Child=questionText,Visibility=Visibility.Collapsed
   };
   body.Children.Add(questionBanner);Grid.SetRow(questionBanner,3);
   questionBanner.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {
    if(!archiveMode||archive.Busy)return;
    if(archive.Slot.Paused){archive.AskNext();e.Handled=true;return;}
    if(archive.LatestQuestion.Length>0)return;
    if(!archive.CanAsk) {archive.Notify("出题需要接通 DeepSeek：请在「设置 → AI 对话」里启用在线 AI 并配置 API Key。");pet.OpenPanel(PanelTab.Settings);return;}
    archive.AskNext();
    e.Handled=true;
   };
   view=new ChatView(true){Margin=new Thickness(14,0,12,8),MaxHeight=268};
   view.Visibility=showHistory?Visibility.Visible:Visibility.Collapsed;
   body.Children.Add(view);Grid.SetRow(view,4);
   archiveView=new ChatView(true){Margin=new Thickness(14,0,12,8),MaxHeight=268,Visibility=Visibility.Collapsed};
   body.Children.Add(archiveView);Grid.SetRow(archiveView,4);
   var inputHost=new Border{
    Margin=new Thickness(14,0,14,0),CornerRadius=new CornerRadius(16),
    Background=Theme.Brush("#FFFFFF"),BorderBrush=Theme.Brush("#D9BAB6"),BorderThickness=new Thickness(1),
    Padding=new Thickness(12,2,4,2)
   };
   var inputGrid=new Grid();
   inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
   inputGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var field=new Grid();
   input=ChatInput.Field();
   input.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   input.MinHeight=40;input.MaxHeight=92;input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
   field.Children.Add(input);
   inputPlaceholder=ChatInput.Placeholder(input,"和曳尔沐说点什么…  /搜 联网 · /逛B站",new Thickness(2,0,0,0));
   field.Children.Add(inputPlaceholder);
   inputGrid.Children.Add(field);Grid.SetColumn(field,0);
   send=new SendButton(delegate {SendNow();},36){Margin=new Thickness(5,4,3,4)};
   inputGrid.Children.Add(send);Grid.SetColumn(send,1);
   inputHost.Child=inputGrid;
   body.Children.Add(inputHost);Grid.SetRow(inputHost,5);
   var footer=new Grid{Margin=new Thickness(18,7,16,12)};
   footer.ColumnDefinitions.Add(new ColumnDefinition());
   footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   hintText=new TextBlock{Text="Enter 发送 · Shift+Enter 换行 · Esc 收起",FontSize=9.5,Foreground=Theme.TextDim};
   footer.Children.Add(hintText);Grid.SetColumn(hintText,0);
   metaText=new TextBlock{Text="待命",FontSize=9.5,Foreground=Theme.Brush("#447969"),HorizontalAlignment=HorizontalAlignment.Right,MaxWidth=250,TextTrimming=TextTrimming.CharacterEllipsis};
   footer.Children.Add(metaText);Grid.SetColumn(metaText,1);
   body.Children.Add(footer);Grid.SetRow(footer,6);
   Content=root;
   StyleMode();ApplyModeVisibility();
   MouseButtonEventHandler grab=delegate(object sender,MouseButtonEventArgs e) {
    if(e.ChangedButton!=MouseButton.Left||e.ButtonState!=MouseButtonState.Pressed)return;
    double beforeLeft=Left,beforeTop=Top;
    try {DragMove();} catch {}
    if(Left!=beforeLeft||Top!=beforeTop) {userMoved=true;bottomAnchor=Top+ActualHeight;ClampToScreen();SavePosition();}
    e.Handled=true;
   };
   face.MouseLeftButtonDown+=grab;names.MouseLeftButtonDown+=grab;
   input.TextChanged+=delegate {UpdateSend();};
   ChatInput.EnterSends(input,SendNow);
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    if(e.Key==Key.Escape) {HideBar();e.Handled=true;}
   };
   Loaded+=delegate {if(!placed)Place();RefreshStatus();RefreshArchiveSlot();UpdateSend();};
   Activated+=delegate {if(IsVisible)FocusInput();};
   SizeChanged+=delegate {
    if(!double.IsNaN(bottomAnchor)&&IsLoaded) {Top=bottomAnchor-ActualHeight;ClampToScreen();}
   };
   Deactivated+=delegate {if(IsVisible&&pet.State.ChatBarAutoHide&&!chat.Busy&&!archive.Busy)HideBar();};
   chat.LineAdded+=OnLine;
   chat.Changed+=OnChanged;
   chat.Cleared+=OnCleared;
   archive.Changed+=OnArchiveChanged;
   ChatLine[] history=chat.Lines;
   for(int i=0;i<history.Length;i++)view.Add(history[i]);
   Closed+=delegate {
    chat.LineAdded-=OnLine;
    chat.Changed-=OnChanged;
    chat.Cleared-=OnCleared;
    archive.Changed-=OnArchiveChanged;
   };
   RefreshStatus();RefreshArchiveSlot();UpdateSend();
  }
  Path GlyphPath(string data) {
   return new Path{
    Data=Geometry.Parse(data),Width=11,Height=11,Stretch=Stretch.Uniform,
    Stroke=Theme.TextSoft,StrokeThickness=1.4,
    StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round
   };
  }
  Border IconButton(Path icon,string tip,Action action) {
   var button=new Border{
    Width=26,Height=26,CornerRadius=new CornerRadius(13),Background=Brushes.Transparent,
    Child=icon,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(3,0,0,0),
    Cursor=Cursors.Hand,ToolTip=tip
   };
   button.MouseEnter+=delegate {button.Background=Theme.Brush("#F4DCE4");icon.Stroke=Theme.TextMain;};
   button.MouseLeave+=delegate {
    button.Background=Brushes.Transparent;
    icon.Stroke=icon==historyIcon&&!showHistory?Theme.Brush("#795F69"):Theme.TextSoft;
   };
   button.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {e.Handled=true;action();};
   return button;
  }
  Border ModePill(string label,Action action,out TextBlock labelText) {
   labelText=new TextBlock{Text=label,FontSize=11,Foreground=Theme.TextDim};
   var pill=new Border{
    CornerRadius=new CornerRadius(9),Padding=new Thickness(11,4,11,4),Margin=new Thickness(0,0,4,0),
    Background=Brushes.Transparent,Cursor=Cursors.Hand,Child=labelText
   };
   pill.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {e.Handled=true;if(action!=null)action();};
   pill.MouseEnter+=delegate {if(!PillActive(pill))pill.Background=Theme.Brush("#F4DCE4");};
   pill.MouseLeave+=delegate {if(!PillActive(pill))pill.Background=Brushes.Transparent;};
   return pill;
  }
  bool PillActive(Border pill) {return pill==chatPill?!archiveMode:archiveMode;}
  void StyleMode() {
   if(chatPill==null)return;
   chatPill.Background=archiveMode?Brushes.Transparent:Theme.Brush("#F4DCE4");
   chatLabel.Foreground=archiveMode?Theme.TextDim:Theme.Accent;
   archivePill.Background=archiveMode?Theme.Brush("#F4DCE4"):Brushes.Transparent;
   archiveLabel.Foreground=archiveMode?Theme.Accent:Theme.TextDim;
   archiveSlotText.Visibility=archiveMode?Visibility.Visible:Visibility.Collapsed;
  }
  void ApplyModeVisibility() {
   bool full=showHistory;
   if(view!=null)view.Visibility=!archiveMode&&full?Visibility.Visible:Visibility.Collapsed;
   if(archiveView!=null)archiveView.Visibility=archiveMode&&full?Visibility.Visible:Visibility.Collapsed;
  }
  void SetMode(bool on) {
   if(archiveMode==on) {if(on) {archive.EnsureQuestion();RebuildArchive();UpdateQuestionBanner();}FocusInput();return;}
   archiveMode=on;
   if(on) {archive.EnsureQuestion();RebuildArchive();}
   StyleMode();ApplyModeVisibility();
   if(!on) inputPlaceholder.Text="和曳尔沐说点什么…  Enter 发送";
   hintText.Text=on?"回答会整理进画像 · 存档自动保存":"Enter 发送 · Shift+Enter 换行 · Esc 收起";
   UpdateQuestionBanner();
   RefreshStatus();UpdateSend();
   if(on) {archiveView.UpdateLayout();}
   FocusInput();
  }
  public void ShowArchive() {SetMode(true);}
  void ToggleHistory() {
   showHistory=!showHistory;
   historyIcon.Data=Geometry.Parse(showHistory?GlyphCollapse:GlyphExpand);
   historyIcon.Stroke=showHistory?Theme.TextSoft:Theme.Brush("#795F69");
   historyButton.ToolTip=showHistory?"收起对话记录":"展开对话记录";
   ApplyModeVisibility();
   if(showHistory) {
    if(archiveMode)RebuildArchive(); else view.UpdateLayout();
   }
   UpdateQuestionBanner();
   if(pet.IsPreview)return;
   pet.State.ChatBarHistory=showHistory;pet.Save();
  }
  void OnLine(ChatLine line) {view.Add(line);}
  void OnArchiveChanged() {
   RefreshArchiveSlot();
   if(archiveMode)RebuildArchive();
   RefreshStatus();UpdateSend();UpdateQuestionBanner();
  }
  void OnChanged() {
   if(!archiveMode)metaText.Text=chat.LastMeta;
   RefreshStatus();UpdateSend();
  }
  void OnCleared() {
   view.ShowEmpty("对话记忆已清空。想在桌面上聊点什么？");
   if(!archiveMode)metaText.Text="已清空";
  }
  void SendNow() {
   if(archiveMode) {SendArchive();return;}
   if(chat.Busy) {metaText.Text="等这条回复回来…";return;}
   string text=(input.Text??"").Trim();
   if(text.Length==0)return;
   input.Clear();
   chat.Send(text);
   input.Focus();
  }
  void SendArchive() {
   if(archive.Busy) {metaText.Text="等这条处理完…";return;}
   if(!archive.HasQuestion) {metaText.Text="先让曳尔沐问一个问题…";return;}
   string text=(input.Text??"").Trim();
   if(text.Length==0)return;
   if(archive.Answer(text))input.Clear();
   input.Focus();
  }
  void UpdateSend() {
   if(archiveMode)send.Ready=!archive.Busy&&!archive.Slot.Paused&&archive.HasQuestion&&(input.Text??"").Trim().Length>0;
   else send.Ready=!chat.Busy&&(input.Text??"").Trim().Length>0;
  }
  void RefreshStatus() {
   if(archiveMode) {
    if(archive.Busy) {headerStatus.Text="◌ "+(archive.BusyTitle.Length>0?archive.BusyTitle:"正在处理…");headerStatus.Foreground=Theme.Lav;}
    else if(archive.Slot==null) {headerStatus.Text="○ 还没有存档";headerStatus.Foreground=Theme.TextDim;}
    else {headerStatus.Text="● 存档单元 · "+ArchiveStore.Stats(archive.Slot);headerStatus.Foreground=Theme.Brush("#447969");}
    metaText.Text=archive.Status.Length>0?archive.Status:"待命";
    return;
   }
   var key=ApiKey.Resolve(pet.State);
   if(chat.Busy) {headerStatus.Text="◌ 正在接通 DeepSeek…";headerStatus.Foreground=Theme.Lav;}
   else if(pet.State.Sleeping) {headerStatus.Text="☾ 休息中 · 说一句话就会醒来";headerStatus.Foreground=Theme.TextDim;}
   else if(!pet.State.AiChat) {headerStatus.Text="○ 离线对白模式";headerStatus.Foreground=Theme.TextDim;}
   else if(!key.Configured) {headerStatus.Text="○ 未配置 API Key · 仍可离线聊天";headerStatus.Foreground=Theme.Brush("#8F642D");}
   else {headerStatus.Text="● 已接入 DeepSeek · 随时可以说话";headerStatus.Foreground=Theme.Brush("#447969");}
   metaText.Text=chat.LastMeta;
  }
  void RefreshArchiveSlot() {
   if(archiveSlotText==null)return;
   ArchiveSlot slot=archive.Slot;
   if(slot==null) {archiveSlotText.Text="没有存档";archiveSlotText.ToolTip="存档单元";return;}
   archiveSlotText.Text=slot.Name+" · "+slot.Items.Count+" 条 ›";
   archiveSlotText.ToolTip="切换存档 / 新建存档 · "+ArchiveStore.Stats(slot);
  }
  void UpdateQuestionBanner() {
   if(questionText==null)return;
   string question=archive.LatestQuestion;
   bool has=question.Length>0;
   if(archive.Slot.Paused){questionText.Text="访谈已暂停 · 点这里继续 ›";questionBanner.Cursor=Cursors.Hand;}
   else if(has) {
    questionText.Text="当前问题："+question;
    questionBanner.Cursor=Cursors.Arrow;
    questionBanner.ToolTip=null;
   } else if(archive.Busy) {
    questionText.Text=archive.BusyTitle.Length>0?archive.BusyTitle:"正在处理…";
    questionBanner.Cursor=Cursors.Arrow;
    questionBanner.ToolTip=null;
   } else if(archive.CanAsk) {
    questionText.Text="还没有问题。点这里让曳尔沐主动问你一个 ›";
    questionBanner.Cursor=Cursors.Hand;
    questionBanner.ToolTip="由 DeepSeek 生成下一个问题";
   } else {
    questionText.Text="出题需要接通 DeepSeek。点这里打开设置 ›";
    questionBanner.Cursor=Cursors.Hand;
    questionBanner.ToolTip="在「设置 → AI 对话」里启用在线 AI 并配置 API Key";
   }
   questionBanner.Visibility=archiveMode&&(archive.Slot.Paused||!showHistory||!has)?Visibility.Visible:Visibility.Collapsed;
   if(archiveMode) inputPlaceholder.Text=has?"回答她的问题，整理进存档…  Enter 发送":"先让曳尔沐问一个问题…";
  }
  void RebuildArchive() {
   if(archiveView==null)return;
   archiveView.Clear();
   ArchiveSlot slot=archive.Slot;
   if(slot==null) {archiveView.ShowEmpty("还没有存档。");return;}
   int start=Math.Max(0,slot.Turns.Count-50);
   for(int i=start;i<slot.Turns.Count;i++) {
    ArchiveTurn turn=slot.Turns[i];
    DateTime at=turn.At==DateTime.MinValue?DateTime.Now:turn.At.ToLocalTime();
    archiveView.Add(new ChatLine(turn.Role=="ask"?ChatSpeaker.Elyms:ChatSpeaker.User,turn.Text,"",at));
   }
   if(archive.Notice.Length>0)archiveView.Add(new ChatLine(ChatSpeaker.Notice,archive.Notice,""));
   if(slot.Turns.Count==0&&archive.Notice.Length==0)archiveView.ShowEmpty("点「存档」旁边的存档名可以切换；先让曳尔沐问一个问题吧。");
  }
  void OpenSlotMenu() {
   var menu=MenuTheme.Create();menu.PlacementTarget=archiveSlotText;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;
   ArchiveSlot current=archive.Slot;
   var slots=archive.Book.Slots;
   for(int i=0;i<slots.Count;i++) {
    ArchiveSlot slot=slots[i];
    var item=new MenuItem{Header=(current!=null&&slot.Id==current.Id?"● ":"")+slot.Name+"  "+slot.Items.Count,IsEnabled=!archive.Busy,Foreground=Theme.TextMain,Padding=new Thickness(10,9,10,9)};
    ArchiveSlot target=slot;
    item.Click+=delegate {archive.Select(target);archive.EnsureQuestion();};
    menu.Items.Add(item);
   }
   menu.Items.Add(new Separator());
   var create=MenuTheme.Item("新建存档","\uE710",null);
   create.IsEnabled=!archive.Busy;create.Click+=delegate {archive.Create("");archive.EnsureQuestion();};
   menu.Items.Add(create);
   var manage=MenuTheme.Item("在陪伴面板里管理…","\uE713",null);
   manage.Click+=delegate {pet.OpenPanel(PanelTab.Archive);};
   menu.Items.Add(manage);
   menu.IsOpen=true;
  }
  double Scale {get {var source=PresentationSource.FromVisual(this);return source==null?1:source.CompositionTarget.TransformToDevice.M11;}}
  Rect ScreenArea() {
   double scale=Scale;
   double width=ActualWidth>0?ActualWidth:Width;
   double height=ActualHeight>0?ActualHeight:220;
   var point=new System.Drawing.Point((int)((Left+width/2)*scale),(int)((Top+height/2)*scale));
   if(double.IsNaN(Left)||Left<-9000||double.IsNaN(Top)||Top<-9000)point=Forms.Cursor.Position;
   var area=Forms.Screen.FromPoint(point).WorkingArea;
   return new Rect(area.X/scale,area.Y/scale,area.Width/scale,area.Height/scale);
  }
  void Place() {
   placed=true;
   double width=ActualWidth>0?ActualWidth:Width;
   double height=ActualHeight>0?ActualHeight:220;
   Rect petArea=pet.WorkArea();
   double left,top;
   if(pet.State.ChatBarX>-90000&&pet.State.ChatBarY>-90000) {left=pet.State.ChatBarX;top=pet.State.ChatBarY;}
   else {
    left=pet.Left+pet.Width/2-width/2;
    top=pet.Top-height-10;
    if(top<petArea.Top+10) {left=petArea.Left+(petArea.Width-width)/2;top=petArea.Bottom-height-72;}
   }
   Left=left;Top=top;ClampToScreen();
  }
  void ClampToScreen() {
   if(double.IsNaN(Left)||double.IsNaN(Top))return;
   Rect area=ScreenArea();
   double width=ActualWidth>0?ActualWidth:Width;
   double height=ActualHeight>0?ActualHeight:220;
   Left=Math.Max(area.Left+8,Math.Min(area.Right-width-8,Left));
   Top=Math.Max(area.Top+8,Math.Min(area.Bottom-height-8,Top));
   bottomAnchor=Top+height;
  }
  void SavePosition() {
   if(pet.IsPreview||!userMoved||double.IsNaN(Left)||double.IsNaN(Top))return;
   pet.State.ChatBarX=Left;pet.State.ChatBarY=Top;pet.Save();
  }
  void FocusInput() {
   Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate {
    if(!IsActive)return;
    input.Focus();Keyboard.Focus(input);input.CaretIndex=input.Text.Length;
   }));
  }
  public void ShowBar() {
   if(IsVisible) {Activate();ClampToScreen();RefreshStatus();RefreshArchiveSlot();if(archiveMode){archive.EnsureQuestion();RebuildArchive();}UpdateQuestionBanner();UpdateSend();FocusInput();return;}
   Show();Activate();ClampToScreen();RefreshStatus();RefreshArchiveSlot();if(archiveMode){archive.EnsureQuestion();RebuildArchive();}UpdateQuestionBanner();UpdateSend();FocusInput();
  }
  public void HideBar() {
   if(!IsLoaded)return;
   SavePosition();Hide();
  }
 }
}
