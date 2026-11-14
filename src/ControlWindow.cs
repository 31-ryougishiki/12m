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
  Button startFocus,endFocus,restFocus,stopChat,retryChat;
  readonly WrapPanel navigation=new WrapPanel();
  readonly ComboBox compactNavigation=new ComboBox{MinHeight=32,FontSize=14,FontFamily=Theme.ReadingFont,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,8)};
  readonly ChatView chatView;
  readonly SendButton sendCircle;
  readonly PasswordBox apiKeyBox;
  readonly CheckBox aiChatBox;
  bool testBusy;
  string lastAiStatus="";
  DateTime nextAiStatusCheck=DateTime.MinValue;
  PluginPage archivePanel;
  PluginPage domainPanel;
  PluginPage dshPanel;
  WebPanel webPanel;
  PluginPage biliPanel;
  PluginPanel pluginPanel;
  readonly System.Collections.Generic.Dictionary<PanelTab,PluginPage> pluginPages=new System.Collections.Generic.Dictionary<PanelTab,PluginPage>();
  public TabControl Tabs;
  Button relatedPage;
  public ReadingZoom ReadingView {get;private set;}

  TextBlock pageTitle,pageDescription;
  static string Description(PanelTab id) {
   switch(id) {
    case PanelTab.Plugins:return "按需安装，只留下你需要的功能。";
    case PanelTab.Cloud:return "备份共享知识数据，保留独立版本。";
    case PanelTab.Companion:return "聊聊今天的事，我在这里。";
    case PanelTab.Focus:return "给自己一段不被打扰的时间。";
    case PanelTab.Notes:return "记下待办，也留住一闪而过的灵感。";
    case PanelTab.Settings:return "调整连接与陪伴方式，让这里更适合你。";
    case PanelTab.Archive:return "把值得留下的片段，好好收藏。";
    case PanelTab.Graph:return "为一个领域连接知识，留下可复用的实现与经验。";
    case PanelTab.Memory:return "查看和整理她记住的事。";
    case PanelTab.Workbench:return "查看委托任务与执行进度。";
    case PanelTab.Web:return "搜索资料，查看联网记录。";
    case PanelTab.Bili:return "一起发现感兴趣的视频。";
    default:return "认识曳尔沐，了解这份桌面陪伴。";
   }
  }
  void RefreshNavigation() {
   if(!SelectedTab.HasValue)return;
   foreach(ComboBoxItem item in compactNavigation.Items)if((PanelTab)item.Tag==SelectedTab.Value){compactNavigation.SelectedItem=item;break;}
   foreach(Button nav in navigation.Children){bool selected=(PanelTab)nav.Tag==SelectedTab.Value;nav.Background=selected?Theme.Accent:Theme.Canvas;nav.Foreground=selected?Brush("#FFFFFF"):Theme.TextMain;}
   pageTitle.Text=((TabItem)Tabs.SelectedItem).Header.ToString();
   pageDescription.Text=Description(SelectedTab.Value);
   Title="曳尔沐 · "+pageTitle.Text;
   if(relatedPage!=null){var id=SelectedTab.Value;relatedPage.Visibility=id==PanelTab.Web||id==PanelTab.Bili||id==PanelTab.Settings||id==PanelTab.Character||id==PanelTab.Archive||id==PanelTab.Memory?Visibility.Visible:Visibility.Collapsed;relatedPage.Content=id==PanelTab.Web?"B站逛逛":id==PanelTab.Bili?"返回联网":id==PanelTab.Settings?"关于曳尔沐":id==PanelTab.Character?"返回设置":id==PanelTab.Archive?"记忆管理":"返回存档";}
  }
  public static SolidColorBrush Brush(string hex) {return Theme.Brush(hex);}
  public static TextBlock Text(string text,double size,string color) {return new TextBlock{Text=text,FontFamily=size>=18?Theme.Font:Theme.ReadingFont,FontSize=Math.Max(12,size),Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};}
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
   var outer=new Grid{Background=Background};
   Button zoomButton=null;
   ReadingView=new ReadingZoom(this,outer,pet.State.ReadingScale,delegate(double value){pet.State.ReadingScale=value;if(zoomButton!=null)zoomButton.Content=Math.Round(value*100)+"%";pet.Save();});Content=ReadingView;
   var content=new DockPanel{Margin=new Thickness(28,20,28,20)};outer.Children.Add(content);
   connection=Text("● 信号满格",12,"#447969");energy=Text("",11,"#78616A");friendship=Text("",11,"#78616A");focusCount=Text("",11,"#78616A");
   var header=new DockPanel{Margin=new Thickness(0,0,0,16)};DockPanel.SetDock(header,Dock.Top);content.Children.Add(header);
   zoomButton=Button(Math.Round(ReadingView.Factor*100)+"%",delegate{ReadingView.SetFactor(1);},false);zoomButton.ToolTip="Ctrl + 滚轮调整整体字号（80%—200%）；Ctrl + 0 或点击这里恢复 100%";zoomButton.Padding=new Thickness(8,6,8,6);zoomButton.Margin=new Thickness(10,0,0,0);zoomButton.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(zoomButton,Dock.Right);header.Children.Add(zoomButton);
   var back=Button("◉  功能圆盘",pet.OpenPanel,false);back.Margin=new Thickness(0,0,20,0);back.VerticalAlignment=VerticalAlignment.Center;back.ToolTip="Ctrl + Alt + E 呼出圆盘；当前输入会保留";DockPanel.SetDock(back,Dock.Left);header.Children.Add(back);
   relatedPage=Button("",delegate{var id=SelectedTab;SelectTab(id==PanelTab.Web?PanelTab.Bili:id==PanelTab.Bili?PanelTab.Web:id==PanelTab.Settings?PanelTab.Character:id==PanelTab.Character?PanelTab.Settings:id==PanelTab.Archive?PanelTab.Memory:PanelTab.Archive);},false);
   relatedPage.VerticalAlignment=VerticalAlignment.Center;relatedPage.Margin=new Thickness(12,0,0,0);DockPanel.SetDock(relatedPage,Dock.Right);header.Children.Add(relatedPage);
   pageTitle=Text("陪伴",24,"#4F3B43");pageTitle.FontWeight=FontWeights.SemiBold;pageTitle.Margin=new Thickness(0,0,0,3);
   pageDescription=Text(Description(PanelTab.Companion),12,"#795F69");pageDescription.Margin=new Thickness(0);
   header.Children.Add(Stack(pageTitle,pageDescription));
   SizeChanged+=delegate {pageDescription.Visibility=ActualHeight<650?Visibility.Collapsed:Visibility.Visible;};
   var navigationScroll=new ScrollViewer{Content=navigation,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=100,Margin=new Thickness(0,0,0,10)};
   var navigationHost=new Grid();navigationHost.Children.Add(navigationScroll);navigationHost.Children.Add(compactNavigation);
   DockPanel.SetDock(navigationHost,Dock.Top);content.Children.Add(navigationHost);
   compactNavigation.SelectionChanged+=delegate{var item=compactNavigation.SelectedItem as ComboBoxItem;if(item!=null&&SelectedTab!=(PanelTab)item.Tag)SelectTab((PanelTab)item.Tag);};
   var tabs=new TabControl{Background=Background,BorderThickness=new Thickness(0)};
   tabs.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TabControl'><ContentPresenter Name='PART_SelectedContentHost' ContentSource='SelectedContent'/></ControlTemplate>");
   content.Children.Add(tabs);Tabs=tabs;
   tabs.SelectionChanged+=delegate {RefreshNavigation();};
   var home=new DockPanel();
   aiStatus=Text("",12,"#447969");
   var chatHead=new Grid{Margin=new Thickness(0,0,0,9)};
   chatHead.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   chatHead.ColumnDefinitions.Add(new ColumnDefinition());
   chatHead.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var face=new Border{Width=40,Height=40,CornerRadius=new CornerRadius(0),Background=new ImageBrush(pet.Art.Face){Stretch=Stretch.UniformToFill},BorderBrush=Brush("#D5B9D6"),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};
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
   var chatFrame=new Border{Background=Brush("#F8FAFF"),BorderBrush=Brush("#8996B5"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(0),Padding=new Thickness(10,8,6,8),Margin=new Thickness(0,0,0,10),Child=chatView};
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
   var inputBorder=new Border{CornerRadius=new CornerRadius(0),Background=Brush("#FFFFFF"),BorderBrush=Brush("#8996B5"),BorderThickness=new Thickness(1),Padding=new Thickness(12,2,4,2),Margin=new Thickness(0,0,0,8),Child=inputGrid};
   reply=Text("在这里哦。想聊些什么？",12.5,"#78616A");
   chatHint=Text("Enter 发送 · Shift + Enter 换行 · /搜 关键词 · /逛B站",11,"#795F69");
   ChatInput.EnterSends(chatInput,SendChat);
   chatInput.TextChanged+=delegate {UpdateSendState();};
   var conversation=new DockPanel();
   var conversationHeader=Stack(chatHead,aiStatus);DockPanel.SetDock(conversationHeader,Dock.Top);conversation.Children.Add(conversationHeader);
   stopChat=Button("停止回复",delegate{pet.Conversation.Stop();},false);
   retryChat=Button("重新编辑上一条",delegate{if(!pet.Conversation.Busy){chatInput.Text=pet.Conversation.LastUserInput??"";chatInput.Focus();}},false);
   var actions=Row(Button("配置 AI",delegate{SelectTab(PanelTab.Settings);},false),Button("开始专注",delegate{SelectTab(PanelTab.Focus);},false),Button("记便签",delegate{SelectTab(PanelTab.Notes);},false),stopChat,retryChat);
   foreach(Button action in actions.Children){action.FontSize=13;action.Padding=new Thickness(9,6,9,6);action.Margin=new Thickness(0,0,6,3);}
   var composer=Stack(reply,inputBorder,chatHint,actions);DockPanel.SetDock(composer,Dock.Bottom);conversation.Children.Add(composer);
   conversation.Children.Add(chatFrame);home.Children.Add(Card(conversation));
   pet.Conversation.LineAdded+=OnChatLine;
   pet.Conversation.Changed+=OnChatChanged;
   pet.Conversation.Cleared+=OnChatCleared;
   pet.Autonomy.Changed+=OnAutonomyChanged;
   AddTab(tabs,PanelTab.Companion,"陪伴",home);
   AddTab(tabs,PanelTab.Archive,"存档单元",new StackPanel());
   AddTab(tabs,PanelTab.Graph,"知识图谱",new StackPanel());
   AddTab(tabs,PanelTab.Workbench,"DSH 工作台",new StackPanel());
   webPanel=new WebPanel(pet);AddTab(tabs,PanelTab.Web,"联网",webPanel.Root);
   AddTab(tabs,PanelTab.Bili,"B站",new StackPanel());
   AddTab(tabs,PanelTab.Cloud,"云同步",new StackPanel());
   pluginPanel=new PluginPanel(pet,SelectTab);AddTab(tabs,PanelTab.Plugins,"插件管理",pluginPanel.Root);
   tabs.SelectionChanged+=delegate {if(SelectedTab.HasValue)EnsurePluginPage(SelectedTab.Value);if(SelectedTab==PanelTab.Plugins)pluginPanel.Refresh();if(SelectedTab==PanelTab.Web&&webPanel!=null)webPanel.Refresh();if(SelectedTab==PanelTab.Bili&&biliPanel!=null)biliPanel.Refresh();};
   focusClock=Text("25:00",60,"#AD506B");focusClock.FontFamily=new FontFamily("Consolas");focusClock.HorizontalAlignment=HorizontalAlignment.Center;
   minutes=Input(pet.State.FocusMinutes.ToString(),42);minutes.Width=88;minutes.HorizontalAlignment=HorizontalAlignment.Left;
   pause=Button("暂停 / 继续",delegate {pet.State.TogglePause(DateTime.UtcNow);pet.Save();Refresh();},false);
   startFocus=Button("开始专注",delegate {StartFocus(false);},true);
   endFocus=Button("结束计时",delegate {pet.State.CancelFocus();pet.Save();Refresh();},false);
   restFocus=Button("休息 5 分钟",delegate {StartFocus(true);},false);
   var presets=new WrapPanel();foreach(int duration in new[]{25,45,60}){int value=duration;presets.Children.Add(Button(value+" 分钟",delegate{minutes.Text=value.ToString();},false));}
   var timerCard=Stack(Text("一起守住这段时间",18,"#4F3B43"),Text("计时中暂停散步和随机闲聊。完成后用气泡提醒。",12,"#78616A"),focusClock,Text("专注时长 / 分钟（1—180）",12,"#78616A"),minutes,presets,Row(startFocus,pause,endFocus,restFocus));
   AddTab(tabs,PanelTab.Focus,"专注",Stack(Card(timerCard),Text("计时和暂停状态会保存。程序关闭时不会发出提醒，\n再次打开后会检查已到期的计时。",12,"#795F69")));
   note=Input(pet.State.Note,245);note.AcceptsReturn=true;note.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;note.VerticalContentAlignment=VerticalAlignment.Top;
   saveStatus=Text("便签仅保存在这台电脑。",12,"#447969");
   note.TextChanged+=delegate {pet.State.Note=note.Text;saveStatus.Text="正在编辑 · 关闭面板时自动保存";};
   AddTab(tabs,PanelTab.Notes,"便签",Card(Stack(Text("给未来的自己，留一条消息",18,"#4F3B43"),Text("今天想完成的事，或是不想忘记的小小灵感。",12,"#78616A"),note,Button("保存便签",delegate {SaveNote();},true),saveStatus)));
   var settings=new StackPanel();
   settings.Children.Add(new Expander{Header="陪伴状态",Content=Stack(connection,energy,friendship,focusCount)});
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
    +(pet.HotkeyAvailable?" Ctrl + Alt + E 呼出或收起功能圆盘。":" 圆盘快捷键被占用，请从托盘打开圆盘。")
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
   dshSettings.Children.Add(Row(Button("打开 DSH 工作台",delegate {SelectTab(PanelTab.Workbench);},true),Button("打开 DSH 页面",delegate {pet.Dsh.OpenWeb();},false)));
   dshSettings.Children.Add(Text("DSH 桥接由 dsh-elyms-bridge 插件提供；DSH 启动后可用。写操作会先在桌面确认；完整访问只建议在信任 DSH agent 时打开。",11,"#795F69"));
   var auto=new StackPanel();
   auto.Children.Add(Text("自主时间",18,"#4F3B43"));
   auto.Children.Add(Text("大约每 30 分钟，她会自己决定做一件小事：找你聊天、去 B站逛逛、写一段自己的笔记，或在显式允许时让 DSH 做一个小实验。睡眠和专注时会跳过。",12,"#78616A"));
   auto.Children.Add(Check("允许她每约 30 分钟自己安排一段活动",pet.State.AutoLife,delegate(bool v){pet.State.AutoLife=v;pet.Save();if(autonomyStatus!=null)autonomyStatus.Text=pet.Autonomy.Status;}));
   auto.Children.Add(Check("允许自主开发小实验（还需打开 DSH 自动执行；只写 autonomy/）",pet.State.AutoLifeDsh,delegate(bool v){pet.State.AutoLifeDsh=v;pet.Save();}));
   auto.Children.Add(Row(Button("现在让她自己安排",delegate {pet.Autonomy.RunNow();},true),Button("清空聊天上下文",delegate {ClearChat();},false)));
   autonomyStatus=Text(pet.Autonomy.Status,11,"#447969");auto.Children.Add(autonomyStatus);
   auto.Children.Add(Text("聊天上下文上限 1,000,000 tokens，历史保存在 chat-history.json。达到上限时才从最旧的对话开始丢弃；自主活动也会写入同一段上下文。",11,"#795F69"));
   AddTab(tabs,PanelTab.Settings,"设置",Stack(new Expander{Header="AI 连接设置",Content=Card(ai)},new Expander{Header="自主陪伴",Content=Card(auto)},new Expander{Header="DSH 连接与权限",Content=PluginAccess.Enabled(pet.State,"dsh")?Card(dshSettings):PluginPanel.Missing(pet,"dsh",SelectTab).Root},new Expander{Header="桌宠偏好",Content=Card(settings)}));
   AddTab(tabs,PanelTab.Character,"档案",Card(Stack(Text("曳尔沐 ≠ Y",22,"#78669B"),Text("本桌宠呈现曳尔沐：E.T.D 六队副队长，擅长通讯、回应和电子世界中的往来。",14,"#59434B"),Text("身份辨析\n借用形象不代表共享身份。档案专门提醒：使用小 Y 的样子，不能推导出 Y 是她的童年。这里的对白不会把 Y 的生平、与 Exe 的往事写成曳尔沐的亲历。",13,"#78616A"),Text("外观依据\n当前银白长发、粉蓝渐变瞳、蝴蝶发夹、蓝色颈后蝴蝶结与白蓝副官制服。桌宠为 Q 版同人绘制。",13,"#78616A"),Text("资料范围\n已核查官方版本公告、角色演示与社区档案转录，资料整理日期：2026-11-09。详细来源、技能概览和不确定项见随附资料。",12,"#795F69"),Row(Button("角色档案来源",delegate {Process.Start("https://www.ntestation.com/曳尔沐");},true),Button("你提供的参考视频",delegate {Process.Start("https://www.bilibili.com/video/BV11y4v6VEn5/");},false)),Text("AI 对话与联网说明\n在线对话由 DeepSeek V4.1 Flash 生成，输入的文字会发送到 DeepSeek API；联网搜索使用 DeepSeek Search / Fetch，搜索词、网址和网页正文会经过 DeepSeek。密钥保存在本机设置文件；聊天记录和记忆都保存在本机数据文件夹，可以随时查看和删除。\n同人作品 v"+AppVersion.Value+" · 图片由 imagegen 生成；日常对白为原创，不是游戏原声。聊天固定开启思考模式；上下文上限 1M tokens；B站评论发送前需确认；自主活动约每 30 分钟一次。",11,"#795F69"))));
    AddTab(tabs,PanelTab.Memory,"记忆",(memoryPanel=new MemoryPanel(pet)).Root);
   tabs.SelectedIndex=0;RefreshNavigation();
   RefreshKeyHint();
   ChatLine[] history=pet.Conversation.Lines;
   for(int i=0;i<history.Length;i++)chatView.Add(history[i]);
   if(pet.Conversation.Busy)sendCircle.Ready=false;
   reply.Text=pet.Conversation.LastHint;
   BindStatus();
   refreshTimer.Tick+=delegate {Refresh();};
   Loaded+=delegate {Refresh();if(!pet.IsPreview)refreshTimer.Start();};Closing+=delegate {SaveNote();if(archivePanel!=null)archivePanel.Save();pet.Save();};
   Closed+=delegate {refreshTimer.Stop();work.Dispose();pet.Conversation.LineAdded-=OnChatLine;pet.Conversation.Changed-=OnChatChanged;pet.Conversation.Cleared-=OnChatCleared;pet.Autonomy.Changed-=OnAutonomyChanged;if(archivePanel!=null)archivePanel.Dispose();if(domainPanel!=null)domainPanel.Dispose();if(dshPanel!=null)dshPanel.Dispose();if(webPanel!=null)webPanel.Dispose();if(biliPanel!=null)biliPanel.Dispose();if(memoryPanel!=null)memoryPanel.Dispose();PluginPage cloud;if(pluginPages.TryGetValue(PanelTab.Cloud,out cloud))cloud.Dispose();};
   Action adaptive=delegate{bool compact=ActualHeight<650||ActualWidth<900;navigationScroll.Visibility=compact?Visibility.Collapsed:Visibility.Visible;compactNavigation.Visibility=compact?Visibility.Visible:Visibility.Collapsed;chatHead.Visibility=ActualHeight<650?Visibility.Collapsed:Visibility.Visible;};
   SizeChanged+=delegate{adaptive();};Loaded+=delegate{adaptive();};
   Refresh();
  }
  MemoryPanel memoryPanel;
  CheckBox Check(string label,bool value,Action<bool> action) {return Ui.Check(label,value,delegate(bool v){action(v);pet.Save();});}
  void AddTab(TabControl tabs,PanelTab id,string name,UIElement content) {
   var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=content,Padding=new Thickness(0,0,6,0)};
   scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   // The conversation owns its scroll area; keep its composer visible at every window size.
   if(id==PanelTab.Companion)scroll.Content=null;
   tabs.Items.Add(new TabItem{Tag=id,Header=name,Content=id==PanelTab.Companion?content:scroll});
   var nav=Button(name,delegate{SelectTab(id);},false);nav.FontSize=13;nav.FontFamily=Theme.ReadingFont;nav.Padding=new Thickness(10,6,10,6);nav.Margin=new Thickness(0,0,5,5);nav.Tag=id;
   if(PluginAccess.ForPage(id).Length==0||PluginAccess.Page(pet.State,id)){navigation.Children.Add(nav);compactNavigation.Items.Add(new ComboBoxItem{Content=name,Tag=id});}
   RefreshNavigation();
  }

  void SaveNote() {if(pet.IsPreview)return;pet.State.Note=note.Text;saveStatus.Text=Storage.Save(pet.State)?"已保存 · "+DateTime.Now.ToString("HH:mm"):"保存失败，请检查数据文件夹权限。";}
  void StartFocus(bool rest) {
   int n;if(rest)n=5;else if(!int.TryParse(minutes.Text,out n)||n<1||n>180) {PopupDialog.Show(this,"请输入 1 到 180 的整数分钟。","专注时长");return;}
   if(pet.State.Remaining(DateTime.UtcNow)>0) {PopupDialog.Show(this,"已有计时正在进行或暂停，请先结束它，再开始新计时。","已有计时");return;}
   pet.State.Sleeping=false;pet.State.StartFocus(n,rest?"休息":"专注",DateTime.UtcNow);pet.Say(rest?"给眼睛和肩膀放个小假吧。":"这一小段时间，我陪你一起。",6);pet.Save();Refresh();
  }
  PanelTab? SelectedTab {get {var tab=Tabs.SelectedItem as TabItem;return tab==null?null:(PanelTab?)tab.Tag;}}
  void EnsurePluginPage(PanelTab id){
   string plugin=PluginAccess.ForPage(id);if(plugin.Length==0||pluginPages.ContainsKey(id))return;
   PluginPage page;
   try{page=pet.State.Plugins.Enabled(plugin)?pet.State.Plugins.Open(plugin,pet,SelectTab):PluginPanel.Missing(pet,plugin,SelectTab);}
   catch(Exception ex){page=PluginPanel.Missing(pet,plugin,SelectTab);pet.Say(ex.Message,6);}
   pluginPages[id]=page;
   if(id==PanelTab.Archive)archivePanel=page;if(id==PanelTab.Graph)domainPanel=page;if(id==PanelTab.Workbench)dshPanel=page;if(id==PanelTab.Bili)biliPanel=page;
   foreach(TabItem tab in Tabs.Items)if((PanelTab)tab.Tag==id)((ScrollViewer)tab.Content).Content=page.Root;
  }
  public void StartGraphTopic(string topic){SelectTab(PanelTab.Archive);if(archivePanel!=null&&PluginAccess.Enabled(pet.State,"archive"))archivePanel.StartTopic(topic);}
  public void SelectTab(PanelTab id) {
   EnsurePluginPage(id);
   if(id==PanelTab.Graph){Width=Math.Min(1500,SystemParameters.WorkArea.Width-40);Height=Math.Min(1000,SystemParameters.WorkArea.Height-40);Left=Math.Max(SystemParameters.WorkArea.Left,Math.Min(Left,SystemParameters.WorkArea.Right-Width));Top=Math.Max(SystemParameters.WorkArea.Top,Math.Min(Top,SystemParameters.WorkArea.Bottom-Height));}
   foreach(TabItem tab in Tabs.Items)if((PanelTab)tab.Tag==id){Tabs.SelectedItem=tab;break;}
  }
  void OnChatLine(ChatLine line) {chatView.Add(line);}
  void OnChatChanged() {
   chatView.SetActivity(pet.Conversation.Busy?pet.Conversation.ActivityLabel:"");
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
  void UpdateSendState() {if(stopChat!=null)stopChat.IsEnabled=pet.Conversation.CanStop;if(retryChat!=null)retryChat.IsEnabled=!pet.Conversation.Busy&&!string.IsNullOrEmpty(pet.Conversation.LastUserInput);chatHint.Text=chatInput.Text.Length+" / 4000 字 · Enter 发送 · Shift + Enter 换行";sendCircle.Ready=!pet.Conversation.Busy&&(chatInput.Text??"").Trim().Length>0;}
  static string ClipText(string text,int max) {text=(text??"").Trim();return text.Length<=max?text:text.Substring(0,max)+"…";}
  void RefreshKeyHint() {
   if(keyHint==null)return;
   var key=ApiKey.Resolve(pet.State);
   keyHint.Text=key.Configured?"当前密钥："+ApiKey.Mask(key.Key)+"（来源："+key.Origin+"）":"还没有配置密钥。密钥只保存在本机设置文件。";
  }
  void RefreshAiStatus(bool force) {
   if(aiStatus==null)return;
   if(pet.Conversation.Busy) {aiStatus.Text="◌ "+pet.Conversation.LastHint;return;}
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
   if(text.Length>ChatService.MaxInputChars){reply.Text="超过 4000 字，请分段发送；原文已保留。";return;}
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
   pet.State.ApiKey="";pet.State.ProtectedApiKey=null;pet.Save();if(Storage.LastError.Length==0)Storage.ForgetBackups();
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
   if(startFocus!=null){bool active=pet.State.Remaining(DateTime.UtcNow)>0;startFocus.Visibility=active?Visibility.Collapsed:Visibility.Visible;pause.Visibility=active?Visibility.Visible:Visibility.Collapsed;endFocus.IsEnabled=active;restFocus.IsEnabled=!active;minutes.IsEnabled=!active;}
   UpdateSendState();
   if(!IsVisible&&!pet.IsPreview)return;
   chatView.SetActivity(pet.Conversation.Busy?pet.Conversation.ActivityLabel:"");
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




