using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ElymsPet {
 public sealed class ControlWindow : Window {
  readonly PetWindow pet;
  readonly WorkScope work;
  readonly CompanionStatusViewModel statusModel=new CompanionStatusViewModel();
  readonly DispatcherTimer refreshTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
  readonly TextBlock connection,energy,friendship,focusClock,focusCount,reply,saveStatus,aiStatus,aiHint,keyHint,chatHint,contextStatus,autonomyStatus;
  readonly TextBox note,chatInput,minutes,apiBase,apiModel;
  readonly Button pause,testButton;
  readonly ChatView chatView;
  readonly SendButton sendCircle;
  readonly PasswordBox apiKeyBox;
  readonly CheckBox aiChatBox;
  bool testBusy;
  string lastAiStatus="";
  DateTime nextAiStatusCheck=DateTime.MinValue;
  ArchivePanel archivePanel;
  DshPanel dshPanel;
  WebPanel webPanel;
  BiliPanel biliPanel;
  public TabControl Tabs;
  readonly StackPanel navigation=new StackPanel();
  readonly Dictionary<PanelTab,Button> navigationButtons=new Dictionary<PanelTab,Button>();
  TextBlock pageTitle,pageDescription;
  static string Description(PanelTab id) {
   switch(id) {
    case PanelTab.Companion:return "聊聊今天的事，我在这里。";
    case PanelTab.Focus:return "给自己一段不被打扰的时间。";
    case PanelTab.Notes:return "记下待办，也留住一闪而过的灵感。";
    case PanelTab.Settings:return "调整连接与陪伴方式，让这里更适合你。";
    case PanelTab.Archive:return "把值得留下的片段，好好收藏。";
    case PanelTab.Memory:return "查看和整理她记住的事。";
    case PanelTab.Workbench:return "查看委托任务与执行进度。";
    case PanelTab.Web:return "搜索资料，查看联网记录。";
    case PanelTab.Bili:return "一起发现感兴趣的视频。";
    default:return "认识曳尔沐，了解这份桌面陪伴。";
   }
  }
  void RefreshNavigation() {
   if(!SelectedTab.HasValue)return;
   foreach(var entry in navigationButtons) {
    bool active=entry.Key==SelectedTab.Value;
    entry.Value.Background=Brush(active?"#F4DCE4":"#F8E7DE");
    entry.Value.Foreground=Brush(active?"#873F59":"#78616A");
    entry.Value.BorderBrush=Brush(active?"#C58CA0":"#F8E7DE");
    entry.Value.FontWeight=active?FontWeights.SemiBold:FontWeights.Normal;
   }
   pageTitle.Text=((TabItem)Tabs.SelectedItem).Header.ToString();
   pageDescription.Text=Description(SelectedTab.Value);
   if(IsLoaded)navigationButtons[SelectedTab.Value].BringIntoView();
  }
  public static SolidColorBrush Brush(string hex) {var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);b.Freeze();return b;}
  public static TextBlock Text(string text,double size,string color) {return new TextBlock{Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};}
  public static Border Card(UIElement child) {return Ui.Card(child);}
  public static Button Button(string label,Action action,bool primary) {return Ui.Button(label,action,primary);}
  TextBox Input(string text,int height) {return Ui.Input(text,height);}
  WrapPanel Row(params UIElement[] items) {var p=new WrapPanel();foreach(var v in items)p.Children.Add(v);return p;}
  StackPanel Stack(params UIElement[] items) {var p=new StackPanel();foreach(var v in items)p.Children.Add(v);return p;}
  public ControlWindow(PetWindow target) {
   pet=target;
   work=new WorkScope(new DispatcherWorkScheduler(pet.Dispatcher),delegate(string error) {testBusy=false;testButton.IsEnabled=true;aiHint.Text=error;},!pet.IsPreview);
   double initialHeight=SystemParameters.WorkArea.Height-60;
   if(initialHeight<560)initialHeight=560;
   if(initialHeight>840)initialHeight=840;
   Title="曳尔沐 · 陪伴控制台";Width=Math.Min(1080,SystemParameters.WorkArea.Width-40);Height=initialHeight;MinWidth=760;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterScreen;
   Background=Theme.Canvas;Foreground=Theme.TextMain;FontFamily=Theme.Font;FontSize=13;
   UseLayoutRounding=true;SnapsToDevicePixels=true;
   TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
   var outer=new Grid{Background=Background};outer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(200)});outer.ColumnDefinitions.Add(new ColumnDefinition());Content=outer;
   var side=new DockPanel{Margin=new Thickness(16,24,16,16)};
   var sideFrame=new Border{Background=Theme.Sidebar,BorderBrush=Brush("#E9D0C7"),BorderThickness=new Thickness(0,0,1,0),Child=side};outer.Children.Add(sideFrame);
   var brand=Stack(Text("E L Y M S  /  陪伴日常",10,"#795F69"),Text("曳尔沐  ✿",25,"#873F59"));
   var portrait=new Border{Width=42,Height=42,CornerRadius=new CornerRadius(21),Background=Theme.Brush("#F2EAF7"),BorderBrush=Theme.Brush("#DCCCE6"),BorderThickness=new Thickness(1),Padding=new Thickness(3),Child=new Image{Source=pet.Art.Face,Stretch=Stretch.Uniform}};
   var identity=new DockPanel{Margin=new Thickness(0,4,0,18)};DockPanel.SetDock(portrait,Dock.Left);identity.Children.Add(portrait);
   connection=Text("●  信号满格",12,"#447969");connection.Margin=new Thickness(10,10,0,0);identity.Children.Add(connection);brand.Children.Add(identity);
   DockPanel.SetDock(brand,Dock.Top);side.Children.Add(brand);
   energy=Text("",11,"#78616A");friendship=Text("",11,"#78616A");focusCount=Text("",11,"#78616A");
   var footer=Stack(energy,friendship,focusCount,Button("回到桌面  ↗",delegate {Hide();},false));
   footer.Margin=new Thickness(6,16,0,0);DockPanel.SetDock(footer,Dock.Bottom);side.Children.Add(footer);
   var navScroll=new ScrollViewer{Content=navigation,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
   navScroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);side.Children.Add(navScroll);
   var content=new DockPanel{Margin=new Thickness(24,24,24,16)};Grid.SetColumn(content,1);outer.Children.Add(content);
   pageTitle=Text("陪伴",25,"#4F3B43");pageTitle.FontWeight=FontWeights.SemiBold;DockPanel.SetDock(pageTitle,Dock.Top);content.Children.Add(pageTitle);
   pageDescription=Text(Description(PanelTab.Companion),12,"#795F69");pageDescription.Margin=new Thickness(0,0,0,20);DockPanel.SetDock(pageDescription,Dock.Top);content.Children.Add(pageDescription);
   SizeChanged+=delegate {
    bool compact=ActualHeight<650;
    pageDescription.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
    energy.Visibility=friendship.Visibility=focusCount.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
   };
   var tabs=new TabControl{Background=Background,BorderThickness=new Thickness(0)};
   tabs.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TabControl'><ContentPresenter Name='PART_SelectedContentHost' ContentSource='SelectedContent'/></ControlTemplate>");
   content.Children.Add(tabs);Tabs=tabs;
   tabs.SelectionChanged+=delegate {RefreshNavigation();};
   var home=new DockPanel();
   var quickActions=Ui.Row(Button("摸摸头",pet.Pet,false),Button("数据零食",pet.Feed,false),Button("接通来电",pet.Call,false),Button("睡眠 / 唤醒",delegate {pet.SleepToggle();Refresh();},false));
   quickActions.Margin=new Thickness(0,0,0,8);DockPanel.SetDock(quickActions,Dock.Top);home.Children.Add(quickActions);
   aiStatus=Text("",12,"#447969");
   var chatHead=new Grid{Margin=new Thickness(0,0,0,9)};
   chatHead.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   chatHead.ColumnDefinitions.Add(new ColumnDefinition());
   chatHead.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var face=new Border{Width=40,Height=40,CornerRadius=new CornerRadius(20),Background=new ImageBrush(pet.Art.Face){Stretch=Stretch.UniformToFill},BorderBrush=Brush("#D5B9D6"),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
   RenderOptions.SetBitmapScalingMode(face,BitmapScalingMode.HighQuality);
   var chatTitle=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
   chatTitle.Children.Add(new TextBlock{Text="桌面小电台",FontSize=17,Foreground=Brush("#4F3B43")});
   chatTitle.Children.Add(new TextBlock{Text="随时说说话，也可以一起查资料",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=Brush("#795F69"),Margin=new Thickness(0,2,0,0)});
   var floatButton=Button("浮动输入框",delegate {pet.OpenChatBar();},false);
   floatButton.Margin=new Thickness(0);floatButton.Padding=new Thickness(12,8,12,8);floatButton.FontSize=12;floatButton.VerticalAlignment=VerticalAlignment.Center;
   chatHead.Children.Add(face);Grid.SetColumn(face,0);
   chatHead.Children.Add(chatTitle);Grid.SetColumn(chatTitle,1);
   chatHead.Children.Add(floatButton);Grid.SetColumn(floatButton,2);
   chatView=new ChatView(false);
   var chatFrame=new Border{Background=Brush("#FFF8F3"),BorderBrush=Brush("#EEDDD6"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(10,8,6,8),Margin=new Thickness(0,0,0,10),Child=chatView};
   chatInput=ChatInput.Field();
   chatInput.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   chatInput.MinHeight=42;chatInput.MaxHeight=104;chatInput.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
   chatInput.ToolTip="Enter 发送，Shift + Enter 换行；内容会发送给 DeepSeek";
   var fieldGrid=new Grid();
   fieldGrid.Children.Add(chatInput);
   fieldGrid.Children.Add(ChatInput.Placeholder(chatInput,"和曳尔沐说点什么…  Enter 发送",new Thickness(2,0,0,0)));
   var inputGrid=new Grid();
   inputGrid.ColumnDefinitions.Add(new ColumnDefinition());
   inputGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   inputGrid.Children.Add(fieldGrid);Grid.SetColumn(fieldGrid,0);
   sendCircle=new SendButton(delegate {SendChat();},38){Margin=new Thickness(5,5,4,5)};
   inputGrid.Children.Add(sendCircle);Grid.SetColumn(sendCircle,1);
   var inputBorder=new Border{CornerRadius=new CornerRadius(14),Background=Brush("#FFFFFF"),BorderBrush=Brush("#D9BAB6"),BorderThickness=new Thickness(1),Padding=new Thickness(12,2,4,2),Margin=new Thickness(0,0,0,8),Child=inputGrid};
   reply=Text("在这里哦。想聊些什么？",12.5,"#78616A");
   chatHint=Text("Enter 发送 · Shift + Enter 换行 · /搜 关键词 · /逛B站",11,"#795F69");
   ChatInput.EnterSends(chatInput,SendChat);
   chatInput.TextChanged+=delegate {UpdateSendState();};
   var conversation=new DockPanel();
   var conversationHeader=Stack(chatHead,aiStatus);DockPanel.SetDock(conversationHeader,Dock.Top);conversation.Children.Add(conversationHeader);
   var composer=Stack(reply,inputBorder,chatHint);DockPanel.SetDock(composer,Dock.Bottom);conversation.Children.Add(composer);
   conversation.Children.Add(chatFrame);home.Children.Add(Card(conversation));
   pet.Conversation.LineAdded+=OnChatLine;
   pet.Conversation.Changed+=OnChatChanged;
   pet.Conversation.Cleared+=OnChatCleared;
   pet.Autonomy.Changed+=OnAutonomyChanged;
   AddTab(tabs,PanelTab.Companion,"陪伴",home);
   archivePanel=new ArchivePanel(pet,SelectTab);
   AddTab(tabs,PanelTab.Archive,"存档单元",archivePanel.Root);
   dshPanel=new DshPanel(pet);
   AddTab(tabs,PanelTab.Workbench,"DSH 工作台",dshPanel.Root);
   webPanel=new WebPanel(pet);
   AddTab(tabs,PanelTab.Web,"联网",webPanel.Root);
   biliPanel=new BiliPanel(pet);
   AddTab(tabs,PanelTab.Bili,"B站",biliPanel.Root);
   tabs.SelectionChanged+=delegate {if(SelectedTab==PanelTab.Web&&webPanel!=null)webPanel.Refresh();if(SelectedTab==PanelTab.Bili&&biliPanel!=null)biliPanel.RefreshTab();};
   focusClock=Text("25:00",60,"#AD506B");focusClock.FontFamily=new FontFamily("Consolas");focusClock.HorizontalAlignment=HorizontalAlignment.Center;
   minutes=Input(pet.State.FocusMinutes.ToString(),42);minutes.Width=88;minutes.HorizontalAlignment=HorizontalAlignment.Left;
   pause=Button("暂停 / 继续",delegate {pet.State.TogglePause(DateTime.UtcNow);pet.Save();Refresh();},false);
   var timerCard=Stack(Text("一起守住这段时间",18,"#4F3B43"),Text("计时中暂停散步和随机闲聊。完成后用气泡提醒。",12,"#78616A"),focusClock,Text("专注时长 / 分钟（1—180）",12,"#78616A"),minutes,Row(Button("开始专注",delegate {StartFocus(false);},true),pause,Button("结束计时",delegate {pet.State.CancelFocus();pet.Save();Refresh();},false)),Button("休息 5 分钟",delegate {StartFocus(true);},false));
   AddTab(tabs,PanelTab.Focus,"专注",Stack(Card(timerCard),Text("计时和暂停状态会保存。程序关闭时不会发出提醒，\n再次打开后会检查已到期的计时。",12,"#795F69")));
   note=Input(pet.State.Note,245);note.AcceptsReturn=true;note.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;note.VerticalContentAlignment=VerticalAlignment.Top;
   saveStatus=Text("便签仅保存在这台电脑。",12,"#447969");
   note.TextChanged+=delegate {pet.State.Note=note.Text;saveStatus.Text="正在编辑 · 关闭面板时自动保存";};
   AddTab(tabs,PanelTab.Notes,"便签",Card(Stack(Text("给未来的自己，留一条消息",18,"#4F3B43"),Text("今天想完成的事，或是不想忘记的小小灵感。",12,"#78616A"),note,Button("保存便签",delegate {SaveNote();},true),saveStatus)));
   var settings=new StackPanel();
   settings.Children.Add(Text("按你的习惯陪伴",18,"#4F3B43"));
   settings.Children.Add(Check("始终置顶",pet.State.Topmost,delegate(bool v){pet.State.Topmost=v;pet.Topmost=v;}));
   settings.Children.Add(Check("桌面散步",pet.State.Wander,delegate(bool v){pet.State.Wander=v;}));
   settings.Children.Add(Check("完成计时时播放轻提示音",!pet.State.Muted,delegate(bool v){pet.State.Muted=!v;}));
   settings.Children.Add(Check("每 45 分钟提醒喝水（睡眠时暂停）",pet.State.RemindWater,delegate(bool v){pet.State.RemindWater=v;}));
   settings.Children.Add(Check("全屏应用时自动隐藏桌宠",pet.State.FullscreenQuiet,delegate(bool v){pet.State.FullscreenQuiet=v;}));
   settings.Children.Add(Check("根据当前窗口标题偶尔说句话（本地规则）",pet.State.WindowComments,delegate(bool v){pet.State.WindowComments=v;}));
   settings.Children.Add(Check("浮动输入框：失去焦点后自动收起",pet.State.ChatBarAutoHide,delegate(bool v){pet.State.ChatBarAutoHide=v;}));
   settings.Children.Add(Check("让她记住你说过的事（记忆只存在本机）",pet.State.MemoryEnabled,delegate(bool v){pet.State.MemoryEnabled=v;pet.Save();}));
   settings.Children.Add(Text("桌宠大小",13,"#6D535F"));
   var slider=new Slider{Minimum=.65,Maximum=1.6,Value=pet.State.Scale,TickFrequency=.05,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,0,20)};slider.ValueChanged+=delegate {pet.ResizePet(slider.Value);};settings.Children.Add(slider);
   settings.Children.Add(Row(Button("浮动输入框",delegate {pet.OpenChatBar();},false),Button("召回桌宠",pet.Recover,true),Button("鼠标穿透",delegate {pet.SetPassThrough(true);pet.Say("已开启鼠标穿透。双击托盘图标就能恢复。",6);},false)));
   settings.Children.Add(Text("Ctrl + Alt + Q 打开浮动输入框，做别的事情时也能和她说一句话。\n"
    +(pet.ChatHotkeyAvailable?"浮动框快捷键已注册。":"浮动框快捷键被占用，请从右键菜单或这里打开。")
    +(pet.HotkeyAvailable?" Ctrl + Alt + E 可召回桌宠。":" 召回快捷键也被占用，请使用托盘。")
    +"\n双击系统托盘图标可找回桌宠、解除穿透。",12,"#795F69"));
   settings.Children.Add(Button("打开数据文件夹",delegate {Directory.CreateDirectory(Storage.Folder);Process.Start("explorer.exe",Storage.Folder);},false));
   var ai=new StackPanel();
   ai.Children.Add(Text("AI 对话 · DeepSeek V4.1 Flash",18,"#4F3B43"));
   ai.Children.Add(Text("模型 deepseek-flash，由 DeepSeek API 在线生成回复；网络或密钥异常时自动改用离线对白。",12,"#78616A"));
   aiChatBox=Check("启用在线 AI 对话",pet.State.AiChat,delegate(bool v){pet.State.AiChat=v;pet.Save();RefreshAiStatus(true);if(webPanel!=null)webPanel.Refresh();});
   ai.Children.Add(aiChatBox);
   ai.Children.Add(Check("允许联网搜索与读取网页（查询会发送到 DeepSeek，按成功请求计费）",pet.State.WebEnabled,delegate(bool v){pet.State.WebEnabled=v;pet.Save();if(webPanel!=null)webPanel.Refresh();RefreshAiStatus(true);}));
   ai.Children.Add(Text("API Key（只保存在本机设置文件）",13,"#6D535F"));
   apiKeyBox=new PasswordBox{Height=40,Width=330,Background=Brush("#FFFCF9"),Foreground=Brush("#4F3B43"),CaretBrush=Brush("#AD506B"),BorderBrush=Brush("#D6B7AE"),Padding=new Thickness(10,7,10,7),FontSize=13,Margin=new Thickness(0,0,9,9),PasswordChar='\u25CF'};
   ai.Children.Add(Row(apiKeyBox,Button("保存 AI 设置",delegate {SaveAiSettings();},true),Button("清除密钥",delegate {ClearApiKey();},false)));
   keyHint=Text("",11,"#795F69");ai.Children.Add(keyHint);
   ai.Children.Add(Text("接口地址与模型",13,"#6D535F"));
   apiBase=Input(pet.State.ApiBase,40);apiBase.Width=330;apiBase.Margin=new Thickness(0,0,9,9);apiBase.FontFamily=new FontFamily("Consolas");
   apiModel=Input(pet.State.ApiModel,40);apiModel.Width=190;apiModel.Margin=new Thickness(0,0,9,9);apiModel.FontFamily=new FontFamily("Consolas");
   ai.Children.Add(Row(apiBase,apiModel));
   ai.Children.Add(Text("默认 https://api.deepseek.com · deepseek-flash（DeepSeek-V4.1-Flash）",11,"#795F69"));
   ai.Children.Add(Text("思考模式 · 固定开启\n每次回复都会先思考再组织文字；比快速模式更慢，也会多消耗 token。该模式固定开启，不提供关闭选项。",11,"#447969"));
   testButton=Button("测试连接",delegate {TestApi();},false);
   ai.Children.Add(Row(testButton,Button("清空对话记忆",delegate {ClearChat();},false),Button("管理密钥与余额",delegate {Process.Start("https://platform.deepseek.com/api_keys");},false),Button("打开联网页",delegate {SelectTab(PanelTab.Web);},false)));
   aiHint=Text("密钥只保存在本机设置文件；聊天内容和联网查询会发送到 DeepSeek API。联网使用 Search / Fetch，每次成功请求按 DeepSeek 账单计费。",11,"#447969");ai.Children.Add(aiHint);
   contextStatus=Text(pet.Conversation.ContextLabel,11,"#795F69");ai.Children.Add(contextStatus);
   var dshSettings=new StackPanel();
   dshSettings.Children.Add(Text("DSH 委托",18,"#4F3B43"));
   dshSettings.Children.Add(Text("把实际写代码、改文件、运行命令的任务交给 DSH agent；DSH 在后台会话里完成，桌宠负责转交和汇报。",12,"#78616A"));
   dshSettings.Children.Add(Check("启用 DSH 委托",pet.State.DshEnabled,delegate(bool v){pet.State.DshEnabled=v;pet.Save();}));
   dshSettings.Children.Add(Check("DSH 任务自动执行（跳过逐次确认，谨慎）",pet.State.DshAutoRun,delegate(bool v){pet.State.DshAutoRun=v;pet.Save();}));
   dshSettings.Children.Add(Check("允许完整访问（可执行命令与写入，谨慎）",pet.State.DshFullAccess,delegate(bool v){pet.State.DshFullAccess=v;pet.Save();}));
   dshSettings.Children.Add(Row(Button("打开 DSH 工作台",delegate {SelectTab(PanelTab.Workbench);},true),Button("打开 DSH 页面",delegate {string error;var handshake=DshApi.ReadHandshake(out error);try {Process.Start(handshake!=null&&!string.IsNullOrEmpty(handshake.webUrl)?handshake.webUrl:"http://127.0.0.1:3080");}catch{}},false)));
   dshSettings.Children.Add(Text("DSH 桥接由 dsh-elyms-bridge 插件提供；DSH 启动后可用。写操作会先在桌面确认；完整访问只建议在信任 DSH agent 时打开。",11,"#795F69"));
   var auto=new StackPanel();
   auto.Children.Add(Text("自主时间",18,"#4F3B43"));
   auto.Children.Add(Text("大约每 30 分钟，她会自己决定做一件小事：找你聊天、去 B站逛逛、写一段自己的笔记，或在显式允许时让 DSH 做一个小实验。睡眠和专注时会跳过。",12,"#78616A"));
   auto.Children.Add(Check("允许她每约 30 分钟自己安排一段活动",pet.State.AutoLife,delegate(bool v){pet.State.AutoLife=v;pet.Save();if(autonomyStatus!=null)autonomyStatus.Text=pet.Autonomy.Status;}));
   auto.Children.Add(Check("允许自主开发小实验（还需打开 DSH 自动执行；只写 autonomy/）",pet.State.AutoLifeDsh,delegate(bool v){pet.State.AutoLifeDsh=v;pet.Save();}));
   auto.Children.Add(Row(Button("现在让她自己安排",delegate {pet.Autonomy.RunNow();},true),Button("清空聊天上下文",delegate {ClearChat();},false)));
   autonomyStatus=Text(pet.Autonomy.Status,11,"#447969");auto.Children.Add(autonomyStatus);
   auto.Children.Add(Text("聊天上下文上限 1,000,000 tokens，历史保存在 chat-history.json。达到上限时才从最旧的对话开始丢弃；自主活动也会写入同一段上下文。",11,"#795F69"));
   AddTab(tabs,PanelTab.Settings,"设置",Stack(Card(ai),Card(auto),Card(dshSettings),Card(settings)));
   AddTab(tabs,PanelTab.Character,"档案",Card(Stack(Text("曳尔沐 ≠ Y",22,"#78669B"),Text("本桌宠呈现曳尔沐：E.T.D 六队副队长，擅长通讯、回应和电子世界中的往来。",14,"#59434B"),Text("身份辨析\n借用形象不代表共享身份。档案专门提醒：使用小 Y 的样子，不能推导出 Y 是她的童年。这里的对白不会把 Y 的生平、与 Exe 的往事写成曳尔沐的亲历。",13,"#78616A"),Text("外观依据\n当前银白长发、粉蓝渐变瞳、蝴蝶发夹、蓝色颈后蝴蝶结与白蓝副官制服。桌宠为 Q 版同人绘制。",13,"#78616A"),Text("资料范围\n已核查官方版本公告、角色演示与社区档案转录，资料整理日期：2026-11-09。详细来源、技能概览和不确定项见随附资料。",12,"#795F69"),Row(Button("角色档案来源",delegate {Process.Start("https://www.ntestation.com/曳尔沐");},true),Button("你提供的参考视频",delegate {Process.Start("https://www.bilibili.com/video/BV11y4v6VEn5/");},false)),Text("AI 对话与联网说明\n在线对话由 DeepSeek V4.1 Flash 生成，输入的文字会发送到 DeepSeek API；联网搜索使用 DeepSeek Search / Fetch，搜索词、网址和网页正文会经过 DeepSeek。密钥保存在本机设置文件；聊天记录和记忆都保存在本机数据文件夹，可以随时查看和删除。\n同人作品 v"+AppVersion.Value+" · 图片由 imagegen 生成；日常对白为原创，不是游戏原声。聊天固定开启思考模式；上下文上限 1M tokens；B站评论发送前需确认；自主活动约每 30 分钟一次。",11,"#795F69"))));
    AddTab(tabs,PanelTab.Memory,"记忆",(memoryPanel=new MemoryPanel(pet)).Root);
   navigation.Children.Clear();
   AddNavigationGroup("日常陪伴",PanelTab.Companion,PanelTab.Focus,PanelTab.Notes,PanelTab.Memory,PanelTab.Archive);
   AddNavigationGroup("探索与工具",PanelTab.Workbench,PanelTab.Web,PanelTab.Bili);
   AddNavigationGroup("偏好与关于",PanelTab.Settings,PanelTab.Character);
   tabs.SelectedIndex=0;RefreshNavigation();
   RefreshKeyHint();
   ChatLine[] history=pet.Conversation.Lines;
   for(int i=0;i<history.Length;i++)chatView.Add(history[i]);
   if(pet.Conversation.Busy)sendCircle.Ready=false;
   reply.Text=pet.Conversation.LastHint;
   BindStatus();
   refreshTimer.Tick+=delegate {Refresh();};
   Loaded+=delegate {Refresh();if(!pet.IsPreview)refreshTimer.Start();};Closing+=delegate {SaveNote();if(archivePanel!=null)archivePanel.Save();pet.Save();};
   Closed+=delegate {refreshTimer.Stop();work.Dispose();pet.Conversation.LineAdded-=OnChatLine;pet.Conversation.Changed-=OnChatChanged;pet.Conversation.Cleared-=OnChatCleared;pet.Autonomy.Changed-=OnAutonomyChanged;if(archivePanel!=null)archivePanel.Dispose();if(dshPanel!=null)dshPanel.Dispose();if(webPanel!=null)webPanel.Dispose();if(biliPanel!=null)biliPanel.Dispose();if(memoryPanel!=null)memoryPanel.Dispose();};
   Refresh();
  }
  MemoryPanel memoryPanel;
  CheckBox Check(string label,bool value,Action<bool> action) {return Ui.Check(label,value,delegate(bool v){action(v);pet.Save();});}
  static string NavigationGlyph(PanelTab id) {
   switch(id) {
    case PanelTab.Companion:return "\uE8F2";
    case PanelTab.Focus:return "\uE916";
    case PanelTab.Notes:return "\uE70B";
    case PanelTab.Memory:return "\uEB51";
    case PanelTab.Archive:return "\uE8B7";
    case PanelTab.Workbench:return "\uE756";
    case PanelTab.Web:return "\uE774";
    case PanelTab.Bili:return "\uE714";
    case PanelTab.Settings:return "\uE713";
    default:return "\uE77B";
   }
  }
  void AddNavigationGroup(string title,params PanelTab[] items) {
   var label=Text(title,10,"#795F69");label.Margin=new Thickness(14,6,0,6);navigation.Children.Add(label);
   foreach(var id in items)navigation.Children.Add(navigationButtons[id]);
  }
  void AddTab(TabControl tabs,PanelTab id,string name,UIElement content) {
   var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=content,Padding=new Thickness(0,0,6,0)};
   scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   // The conversation owns its scroll area; keep its composer visible at every window size.
   if(id==PanelTab.Companion)scroll.Content=null;
   tabs.Items.Add(new TabItem{Tag=id,Header=name,Content=id==PanelTab.Companion?content:scroll});
   var nav=Button(name,delegate {SelectTab(id);},false);
   var navContent=new StackPanel{Orientation=Orientation.Horizontal};
   navContent.Children.Add(new TextBlock{Text=NavigationGlyph(id),FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=15,Width=26,VerticalAlignment=VerticalAlignment.Center});
   navContent.Children.Add(new TextBlock{Text=name,VerticalAlignment=VerticalAlignment.Center});
   nav.Content=navContent;System.Windows.Automation.AutomationProperties.SetName(nav,name);
   nav.HorizontalContentAlignment=HorizontalAlignment.Left;nav.HorizontalAlignment=HorizontalAlignment.Stretch;
   nav.Margin=new Thickness(0,0,0,3);nav.Padding=new Thickness(14,6,14,6);
   nav.ToolTip=Description(id);navigationButtons.Add(id,nav);navigation.Children.Add(nav);
   RefreshNavigation();
  }

  void SaveNote() {if(pet.IsPreview)return;pet.State.Note=note.Text;saveStatus.Text=Storage.Save(pet.State)?"已保存 · "+DateTime.Now.ToString("HH:mm"):"保存失败，请检查数据文件夹权限。";}
  void StartFocus(bool rest) {
   int n;if(rest)n=5;else if(!int.TryParse(minutes.Text,out n)||n<1||n>180) {PopupDialog.Show(this,"请输入 1 到 180 的整数分钟。","专注时长");return;}
   if(pet.State.Remaining(DateTime.UtcNow)>0) {PopupDialog.Show(this,"已有计时正在进行或暂停，请先结束它，再开始新计时。","已有计时");return;}
   pet.State.Sleeping=false;pet.State.StartFocus(n,rest?"休息":"专注",DateTime.UtcNow);pet.Say(rest?"给眼睛和肩膀放个小假吧。":"这一小段时间，我陪你一起。",6);pet.Save();Refresh();
  }
  PanelTab? SelectedTab {get {var tab=Tabs.SelectedItem as TabItem;return tab==null?null:(PanelTab?)tab.Tag;}}
  public void SelectTab(PanelTab id) {
   foreach(TabItem tab in Tabs.Items)if((PanelTab)tab.Tag==id){Tabs.SelectedItem=tab;break;}
  }
  void OnChatLine(ChatLine line) {chatView.Add(line);}
  void OnChatChanged() {
   string hint=pet.Conversation.LastHint;
   if(reply.Text!=hint)reply.Text=hint;
   if(contextStatus!=null)contextStatus.Text=pet.Conversation.ContextLabel;
   UpdateSendState();RefreshAiStatus(true);
  }
  void OnAutonomyChanged() {
   if(autonomyStatus!=null)autonomyStatus.Text=pet.Autonomy.Status;
  }
  void OnChatCleared() {
   chatView.ShowEmpty("对话记忆已清空。想聊什么，直接写下来就好。");
   reply.Text="对话记忆已经清空。";if(contextStatus!=null)contextStatus.Text=pet.Conversation.ContextLabel;UpdateSendState();
  }
  void UpdateSendState() {sendCircle.Ready=!pet.Conversation.Busy&&(chatInput.Text??"").Trim().Length>0;}
  static string ClipText(string text,int max) {text=(text??"").Trim();return text.Length<=max?text:text.Substring(0,max)+"…";}
  void RefreshKeyHint() {
   if(keyHint==null)return;
   var key=ApiKey.Resolve(pet.State);
   keyHint.Text=key.Configured?"当前密钥："+ApiKey.Mask(key.Key)+"（来源："+key.Origin+"）":"还没有配置密钥。密钥只保存在本机设置文件。";
  }
  void RefreshAiStatus(bool force) {
   if(aiStatus==null)return;
   if(pet.Conversation.Busy) {if(aiStatus.Text!="◌ 正在接通 DeepSeek…")aiStatus.Text="◌ 正在接通 DeepSeek…";return;}
   if(!force&&DateTime.UtcNow<nextAiStatusCheck)return;
   nextAiStatusCheck=DateTime.UtcNow.AddSeconds(2);
   var resolved=ApiKey.Resolve(pet.State);
   string key=resolved.Key,origin=resolved.Origin;
   string status;
   if(!pet.State.AiChat)status="○ 在线 AI 已关闭 · 使用离线对白";
   else if(!ApiKey.Valid(key))status="○ 尚未配置 API Key · 在「设置」里接入 DeepSeek";
   else if(origin=="设置文件")status="● 已接入 DeepSeek V4.1 Flash · 密钥保存在本机";
   else if(origin=="环境变量 DEEPSEEK_API_KEY")status="● 已接入 DeepSeek V4.1 Flash · 密钥来自环境变量";
   else status="● 已接入 DeepSeek V4.1 Flash · 密钥来自本地文件";
   if(force||status!=lastAiStatus) {lastAiStatus=status;aiStatus.Text=status;}
  }
  void SendChat() {
   if(pet.Conversation.Busy) {reply.Text="等这条回复回来，再说下一句吧。";return;}
   string text=(chatInput.Text??"").Trim();
   if(text.Length==0)return;
   chatInput.Clear();
   pet.Conversation.Send(text);
   if(IsVisible)chatInput.Focus();
  }
  void ClearChat() {
   pet.Conversation.Clear();
   if(IsVisible)chatInput.Focus();
  }
  void SaveAiSettings() {
   if(!DeepSeek.ValidBaseUrl(apiBase.Text)) {aiHint.Text="接口地址不完整，请填写 https://api.deepseek.com 这样的地址。";return;}
   string baseUrl=DeepSeek.NormalizeBaseUrl(apiBase.Text);
   string model=(apiModel.Text??"").Trim();
   if(model.Length==0)model=DeepSeek.DefaultModel;
   if(model.Length>80)model=model.Substring(0,80);
   string pasted=ApiKey.Clean(apiKeyBox.Password);
   if(pasted.Length>0&&!ApiKey.Valid(pasted)) {aiHint.Text="这个密钥看起来不完整，请重新复制完整的 API Key。";return;}
   bool keyChanged=pasted.Length>0;
   if(keyChanged)pet.State.ApiKey=pasted;
   pet.State.ApiBase=baseUrl;pet.State.ApiModel=model;pet.State.AiThinking=true;
   pet.Save();if(keyChanged&&Storage.LastError.Length==0)Storage.ForgetBackups();
   apiBase.Text=baseUrl;apiModel.Text=model;apiKeyBox.Clear();
   RefreshKeyHint();RefreshAiStatus(true);if(webPanel!=null)webPanel.Refresh();
   aiHint.Text=Storage.LastError.Length>0?"保存失败："+Storage.LastError:"AI 设置已保存。可以在「陪伴」页直接聊天。";
  }
  void ClearApiKey() {
   if(PopupDialog.Show(this,"清除后，本机设置文件里的密钥会被删除。\n如果本地还有 api-key.txt 或环境变量，仍会继续使用。\n确定清除吗？","清除 API Key",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   pet.State.ApiKey="";pet.Save();if(Storage.LastError.Length==0)Storage.ForgetBackups();
   apiKeyBox.Clear();RefreshKeyHint();RefreshAiStatus(true);if(webPanel!=null)webPanel.Refresh();
   aiHint.Text="本机设置文件里的密钥已清除。";
  }
  void TestApi() {
   if(testBusy)return;
   string key=ApiKey.Clean(apiKeyBox.Password);
   if(!ApiKey.Valid(key))key=ApiKey.Resolve(pet.State).Key;
   if(!ApiKey.Valid(key)) {aiHint.Text="先填入并保存 API Key，再测试连接。";return;}
   string baseUrl=apiBase.Text,model=(apiModel.Text??"").Trim();
   if(model.Length==0)model=DeepSeek.DefaultModel;
   testBusy=true;testButton.IsEnabled=false;aiHint.Text="正在测试连接（最多 25 秒）…";
   work.Run(delegate {return DeepSeek.Complete(key,baseUrl,model,false,Persona.Connectivity(),25);},delegate(ChatOutcome outcome) {
     testBusy=false;testButton.IsEnabled=true;
     if(outcome.Ok)aiHint.Text="● 连接正常 · "+model+" · "+outcome.Seconds.ToString("0.0")+"s · 回复："+ClipText(outcome.Text,40);
     else aiHint.Text="○ 连接失败 · "+outcome.Error;
   });
  }
  void BindStatus() {
   Bind(connection,TextBlock.TextProperty,"Connection");Bind(energy,TextBlock.TextProperty,"Energy");
   Bind(friendship,TextBlock.TextProperty,"Friendship");Bind(focusCount,TextBlock.TextProperty,"FocusCount");
   Bind(focusClock,TextBlock.TextProperty,"FocusClock");Bind(pause,System.Windows.Controls.Button.ContentProperty,"PauseLabel");
   Bind(pause,System.Windows.Controls.Button.IsEnabledProperty,"CanPause");
  }
  void Bind(DependencyObject target,DependencyProperty property,string name) {
   BindingOperations.SetBinding(target,property,new Binding(name) {Source=statusModel,Mode=BindingMode.OneWay});
  }
  public void Refresh() {
   if(!IsVisible&&!pet.IsPreview)return;
   statusModel.Refresh(pet.State,DateTime.UtcNow);
   if(pet.Conversation.Busy) {if(sendCircle.Ready)sendCircle.Ready=false;}
   else UpdateSendState();
   if(reply.Text!=pet.Conversation.LastHint)reply.Text=pet.Conversation.LastHint;
   if(contextStatus!=null)contextStatus.Text=pet.Conversation.ContextLabel;
   if(autonomyStatus!=null)autonomyStatus.Text=pet.Autonomy.Status;
   RefreshAiStatus(false);
  }
 }
}
