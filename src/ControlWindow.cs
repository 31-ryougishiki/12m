using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ElymsPet {
 public sealed class ControlWindow : Window {
  readonly PetWindow pet;
  readonly TextBlock connection,energy,friendship,focusClock,focusCount,reply,saveStatus,aiStatus,aiHint,keyHint,chatHint;
  readonly TextBox note,chat,minutes,chatLog,apiBase,apiModel;
  readonly Button pause,sendButton,testButton;
  readonly PasswordBox apiKeyBox;
  readonly CheckBox aiChatBox,aiThinkingBox;
  bool chatBusy,testBusy;
  string lastAiStatus="";
  DateTime nextAiStatusCheck=DateTime.MinValue;
  public TabControl Tabs;
  public static SolidColorBrush Brush(string hex) {var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);b.Freeze();return b;}
  public static TextBlock Text(string text,double size,string color) {return new TextBlock{Text=text,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};}
  public static Border Card(UIElement child) {return new Border{Background=Brush("#19263D"),BorderBrush=Brush("#2B3B58"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(20),Margin=new Thickness(0,0,0,14),Child=child};}
  public static Button Button(string label,Action action,bool primary) {
   var b=new Button{Content=label,Padding=new Thickness(15,10,15,10),Margin=new Thickness(0,0,9,9),Background=Brush(primary?"#AFEDE1":"#293955"),Foreground=Brush(primary?"#132A31":"#E0E7FA"),BorderThickness=new Thickness(0),Cursor=System.Windows.Input.Cursors.Hand,FontSize=13};
   var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
   var cp=new FrameworkElementFactory(typeof(ContentPresenter));cp.SetValue(ContentPresenter.MarginProperty,new TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty));cp.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);border.AppendChild(cp);b.Template=new ControlTemplate(typeof(Button)){VisualTree=border};b.Click+=delegate {action();};return b;
  }
  TextBox Input(string text,int height) {return new TextBox{Text=text,Height=height,Background=Brush("#111D31"),Foreground=Brush("#E6EDFF"),CaretBrush=Brush("#AFEFDF"),BorderBrush=Brush("#3B4C6A"),Padding=new Thickness(12,9,12,9),FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),VerticalContentAlignment=VerticalAlignment.Center};}
  WrapPanel Row(params UIElement[] items) {var p=new WrapPanel();foreach(var v in items)p.Children.Add(v);return p;}
  StackPanel Stack(params UIElement[] items) {var p=new StackPanel();foreach(var v in items)p.Children.Add(v);return p;}
  public ControlWindow(PetWindow target) {
   pet=target;Title="曳尔沐 · 陪伴控制台";Width=850;Height=710;MinWidth=710;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterScreen;
   Background=Brush("#101A2C");Foreground=Brush("#E6EDFF");FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;
   var outer=new Grid{Background=Background};outer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(245)});outer.ColumnDefinitions.Add(new ColumnDefinition());Content=outer;
   var side=new StackPanel{Margin=new Thickness(23,28,18,24)};outer.Children.Add(side);
   side.Children.Add(Text("D E S K T O P   C O N N E C T I O N",10,"#8FA4C7"));side.Children.Add(Text("桌面来电",29,"#EFF3FF"));
   side.Children.Add(Text("曳尔沐  /  ELYMS",13,"#ACB8F5"));
   var portrait=new Image{Source=pet.Art.Frames[0],Height=218,Stretch=Stretch.Uniform,Margin=new Thickness(-6,10,-6,8)};side.Children.Add(portrait);
   connection=Text("●  信号满格",13,"#9FE5D7");side.Children.Add(connection);
   side.Children.Add(Text("把一点温柔，留在你的桌面。",12,"#A4B3CD"));
   energy=Text("",13,"#C8D2EB");friendship=Text("",13,"#C8D2EB");focusCount=Text("",13,"#C8D2EB");side.Children.Add(Card(Stack(energy,friendship,focusCount)));
   side.Children.Add(Text("单击互动 · 双击面板\n拖动移动 · 滚轮缩放\n右键查看更多动作",11,"#8FA4C7"));
   side.Children.Add(Button("回到桌面",delegate {Hide();},false));
   var content=new DockPanel{Margin=new Thickness(12,28,25,20)};Grid.SetColumn(content,1);outer.Children.Add(content);
   var title=Text("今天，也保持联络。",23,"#F0F3FF");DockPanel.SetDock(title,Dock.Top);content.Children.Add(title);
   var subtitle=Text("一小段专注，一条便签，一位在这里的朋友。",12,"#91A4C4");DockPanel.SetDock(subtitle,Dock.Top);content.Children.Add(subtitle);
   var tabs=new TabControl{Background=Brush("#101A2C"),BorderThickness=new Thickness(0),Foreground=Brush("#B8C8E5")};
   var tabStyle=new Style(typeof(TabItem));tabStyle.Setters.Add(new Setter(TabItem.BackgroundProperty,Brush("#1C2A42")));
   tabStyle.Setters.Add(new Setter(TabItem.ForegroundProperty,Brush("#B8C8E5")));
   tabStyle.Setters.Add(new Setter(TabItem.MarginProperty,new Thickness(0,0,6,0)));
   var tabBorder=new FrameworkElementFactory(typeof(Border));tabBorder.SetValue(Border.CornerRadiusProperty,new CornerRadius(9));tabBorder.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(TabItem.BackgroundProperty));
   var tabLabel=new FrameworkElementFactory(typeof(ContentPresenter));tabLabel.SetValue(ContentPresenter.ContentSourceProperty,"Header");tabLabel.SetValue(ContentPresenter.MarginProperty,new Thickness(13,9,13,9));tabBorder.AppendChild(tabLabel);tabStyle.Setters.Add(new Setter(TabItem.TemplateProperty,new ControlTemplate(typeof(TabItem)){VisualTree=tabBorder}));
   var selected=new Trigger{Property=TabItem.IsSelectedProperty,Value=true};selected.Setters.Add(new Setter(TabItem.BackgroundProperty,Brush("#AFEBDF")));selected.Setters.Add(new Setter(TabItem.ForegroundProperty,Brush("#193238")));tabStyle.Triggers.Add(selected);tabs.ItemContainerStyle=tabStyle;content.Children.Add(tabs);
   Tabs=tabs;var home=new StackPanel();
   home.Children.Add(Card(Stack(Text("现在想做些什么？",18,"#E7EDFC"),Text("你的每一次回应，都是新的连接。",12,"#A6B5CF"),Row(Button("摸摸头",pet.Pet,true),Button("数据零食",pet.Feed,false),Button("接通来电",pet.Call,false),Button("睡眠 / 唤醒",delegate {pet.SleepToggle();Refresh();},false)))));
   aiStatus=Text("",12,"#9FE5D7");
   chatLog=Input("",110);chatLog.IsReadOnly=true;chatLog.AcceptsReturn=true;chatLog.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;chatLog.VerticalContentAlignment=VerticalAlignment.Top;chatLog.FontSize=13;chatLog.Margin=new Thickness(0,0,0,10);
   chat=Input("",44);chat.MaxLength=4000;chat.AcceptsReturn=true;chat.VerticalContentAlignment=VerticalAlignment.Top;
   chat.ToolTip="Enter 发送，Shift + Enter 换行；内容会发送给 DeepSeek";
   reply=Text("在这里哦。想聊些什么？",13,"#C6DDD9");
   chatHint=Text("对话记忆只在本次运行中保留；网络或密钥异常时自动改用离线对白。",11,"#8FA4C7");
   sendButton=Button("发送给曳尔沐",delegate {SendChat();},true);
   chat.PreviewKeyDown+=delegate(object o,System.Windows.Input.KeyEventArgs e) {if((e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.Return)&&(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Shift)==0){SendChat();e.Handled=true;}};
   var inputRow=new Grid();inputRow.ColumnDefinitions.Add(new ColumnDefinition());inputRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   inputRow.Children.Add(chat);Grid.SetColumn(chat,0);inputRow.Children.Add(sendButton);Grid.SetColumn(sendButton,1);sendButton.VerticalAlignment=VerticalAlignment.Top;
   home.Children.Add(Card(Stack(Text("桌面小电台 · DeepSeek V4.1 Flash",18,"#E7EDFC"),Text("在线对话，断线时自动改用离线对白。",12,"#A6B5CF"),aiStatus,chatLog,inputRow,reply,chatHint)));
   AddTab(tabs,"陪伴",home);
   focusClock=Text("25:00",60,"#B4F1DF");focusClock.FontFamily=new FontFamily("Consolas");focusClock.HorizontalAlignment=HorizontalAlignment.Center;
   minutes=Input(pet.State.FocusMinutes.ToString(),42);minutes.Width=88;minutes.HorizontalAlignment=HorizontalAlignment.Left;
   pause=Button("暂停 / 继续",delegate {pet.State.TogglePause(DateTime.UtcNow);pet.Save();Refresh();},false);
   var timerCard=Stack(Text("一起守住这段时间",18,"#E7EDFC"),Text("计时中暂停散步和随机闲聊。完成后用气泡提醒。",12,"#A6B5CF"),focusClock,Text("专注时长 / 分钟（1—180）",12,"#A6B5CF"),minutes,Row(Button("开始专注",delegate {StartFocus(false);},true),pause,Button("结束计时",delegate {pet.State.CancelFocus();pet.Save();Refresh();},false)),Button("休息 5 分钟",delegate {StartFocus(true);},false));
   AddTab(tabs,"专注",Stack(Card(timerCard),Text("计时和暂停状态会保存。程序关闭时不会发出提醒，\n再次打开后会检查已到期的计时。",12,"#91A4C4")));
   note=Input(pet.State.Note,245);note.AcceptsReturn=true;note.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;note.VerticalContentAlignment=VerticalAlignment.Top;
   saveStatus=Text("便签仅保存在这台电脑。",12,"#93B9B0");
   note.TextChanged+=delegate {pet.State.Note=note.Text;saveStatus.Text="正在编辑 · 关闭面板时自动保存";};
   AddTab(tabs,"便签",Card(Stack(Text("给未来的自己，留一条消息",18,"#E7EDFC"),Text("今天想完成的事，或是不想忘记的小小灵感。",12,"#A6B5CF"),note,Button("保存便签",delegate {SaveNote();},true),saveStatus)));
   var settings=new StackPanel();
   settings.Children.Add(Text("按你的习惯陪伴",18,"#E7EDFC"));
   settings.Children.Add(Check("始终置顶",pet.State.Topmost,delegate(bool v){pet.State.Topmost=v;pet.Topmost=v;}));
   settings.Children.Add(Check("桌面散步",pet.State.Wander,delegate(bool v){pet.State.Wander=v;}));
   settings.Children.Add(Check("完成计时时播放轻提示音",!pet.State.Muted,delegate(bool v){pet.State.Muted=!v;}));
   settings.Children.Add(Check("每 45 分钟提醒喝水（睡眠时暂停）",pet.State.RemindWater,delegate(bool v){pet.State.RemindWater=v;}));
   settings.Children.Add(Check("全屏应用时自动隐藏桌宠",pet.State.FullscreenQuiet,delegate(bool v){pet.State.FullscreenQuiet=v;}));
   settings.Children.Add(Check("根据当前窗口标题偶尔说句话（本地规则）",pet.State.WindowComments,delegate(bool v){pet.State.WindowComments=v;}));
   settings.Children.Add(Text("桌宠大小",13,"#BAC8E2"));
   var slider=new Slider{Minimum=.65,Maximum=1.6,Value=pet.State.Scale,TickFrequency=.05,IsSnapToTickEnabled=true,Margin=new Thickness(0,0,0,20)};slider.ValueChanged+=delegate {pet.ResizePet(slider.Value);};settings.Children.Add(slider);
   settings.Children.Add(Row(Button("召回桌宠",pet.Recover,true),Button("鼠标穿透",delegate {pet.SetPassThrough(true);pet.Say("已开启鼠标穿透。双击托盘图标就能恢复。",6);},false)));
   settings.Children.Add(Text("双击系统托盘图标可找回桌宠、解除穿透。\n"+(pet.HotkeyAvailable?"快捷键 Ctrl + Alt + E 也可以召回。":"快捷键被占用时，请使用托盘召回。"),12,"#91A4C4"));
   settings.Children.Add(Button("打开数据文件夹",delegate {Directory.CreateDirectory(Storage.Folder);Process.Start("explorer.exe",Storage.Folder);},false));
   var ai=new StackPanel();
   ai.Children.Add(Text("AI 对话 · DeepSeek V4.1 Flash",18,"#E7EDFC"));
   ai.Children.Add(Text("模型 deepseek-flash，由 DeepSeek API 在线生成回复；网络或密钥异常时自动改用离线对白。",12,"#A6B5CF"));
   aiChatBox=Check("启用在线 AI 对话",pet.State.AiChat,delegate(bool v){pet.State.AiChat=v;pet.Save();RefreshAiStatus(true);});
   ai.Children.Add(aiChatBox);
   ai.Children.Add(Text("API Key（只保存在本机设置文件）",13,"#BAC8E2"));
   apiKeyBox=new PasswordBox{Height=40,Width=330,Background=Brush("#111D31"),Foreground=Brush("#E6EDFF"),CaretBrush=Brush("#AFEFDF"),BorderBrush=Brush("#3B4C6A"),Padding=new Thickness(10,7,10,7),FontSize=13,Margin=new Thickness(0,0,9,9),PasswordChar='\u25CF'};
   ai.Children.Add(Row(apiKeyBox,Button("保存 AI 设置",delegate {SaveAiSettings();},true),Button("清除密钥",delegate {ClearApiKey();},false)));
   keyHint=Text("",11,"#8FA4C7");ai.Children.Add(keyHint);
   ai.Children.Add(Text("接口地址与模型",13,"#BAC8E2"));
   apiBase=Input(pet.State.ApiBase,40);apiBase.Width=330;apiBase.Margin=new Thickness(0,0,9,9);apiBase.FontFamily=new FontFamily("Consolas");
   apiModel=Input(pet.State.ApiModel,40);apiModel.Width=190;apiModel.Margin=new Thickness(0,0,9,9);apiModel.FontFamily=new FontFamily("Consolas");
   ai.Children.Add(Row(apiBase,apiModel));
   ai.Children.Add(Text("默认 https://api.deepseek.com · deepseek-flash（DeepSeek-V4.1-Flash）",11,"#8FA4C7"));
   aiThinkingBox=Check("深度思考（更慢，也更费 token）",pet.State.AiThinking,delegate(bool v){pet.State.AiThinking=v;pet.Save();});
   ai.Children.Add(aiThinkingBox);
   testButton=Button("测试连接",delegate {TestApi();},false);
   ai.Children.Add(Row(testButton,Button("清空对话记忆",delegate {ClearChat();},false),Button("管理密钥与余额",delegate {Process.Start("https://platform.deepseek.com/api_keys");},false)));
   aiHint=Text("密钥只保存在本机设置文件；聊天内容会发送到 DeepSeek API。",11,"#93B9B0");ai.Children.Add(aiHint);
   AddTab(tabs,"设置",Stack(Card(ai),Card(settings)));
   AddTab(tabs,"档案",Card(Stack(Text("曳尔沐 ≠ Y",22,"#BFCEF9"),Text("本桌宠呈现曳尔沐：E.T.D 六队副队长，擅长通讯、回应和电子世界中的往来。",14,"#DCE5F7"),Text("身份辨析\n借用形象不代表共享身份。档案专门提醒：使用小 Y 的样子，不能推导出 Y 是她的童年。这里的对白不会把 Y 的生平、与 Exe 的往事写成曳尔沐的亲历。",13,"#A9BBD8"),Text("外观依据\n当前银白长发、粉蓝渐变瞳、蝴蝶发夹、蓝色颈后蝴蝶结与白蓝副官制服。桌宠为 Q 版同人绘制。",13,"#A9BBD8"),Text("资料范围\n已核查官方版本公告、角色演示与社区档案转录，资料整理日期：2026-11-09。详细来源、技能概览和不确定项见随附资料。",12,"#91A4C4"),Row(Button("角色档案来源",delegate {Process.Start("https://www.ntestation.com/曳尔沐");},true),Button("你提供的参考视频",delegate {Process.Start("https://www.bilibili.com/video/BV11y4v6VEn5/");},false)),Text("AI 对话说明\n在线对话由 DeepSeek V4.1 Flash 生成，输入的文字会发送到 DeepSeek API；密钥保存在本机设置文件，聊天记忆只在本次运行中保留。\n同人作品 v1.1 · 图片由 imagegen 生成；日常对白为原创，不是游戏原声。",11,"#8498BA"))));
   RefreshKeyHint();var history=pet.Chat.History;for(int i=0;i<history.Length;i++)AppendLog(history[i].Role=="user"?"你":"曳尔沐",history[i].Content);
   if(pet.AiBusy) {sendButton.IsEnabled=false;chat.IsEnabled=false;}
   Loaded+=delegate {Refresh();};Closing+=delegate {SaveNote();pet.Save();};Refresh();
  }
  CheckBox Check(string label,bool value,Action<bool> action) {var b=new CheckBox{Content=label,IsChecked=value,Foreground=Brush("#D3DEF2"),Margin=new Thickness(0,0,0,20)};b.Click+=delegate {action(b.IsChecked==true);pet.Save();};return b;}
  void AddTab(TabControl tabs,string name,UIElement content) {tabs.Items.Add(new TabItem{Header=name,Padding=new Thickness(13,9,13,9),Content=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=content,Margin=new Thickness(0,18,0,0)}});}
  void SaveNote() {if(pet.IsPreview)return;pet.State.Note=note.Text;saveStatus.Text=Storage.Save(pet.State)?"已保存 · "+DateTime.Now.ToString("HH:mm"):"保存失败，请检查数据文件夹权限。";}
  void StartFocus(bool rest) {
   int n;if(rest)n=5;else if(!int.TryParse(minutes.Text,out n)||n<1||n>180) {MessageBox.Show(this,"请输入 1 到 180 的整数分钟。","专注时长");return;}
   if(pet.State.Remaining(DateTime.UtcNow)>0) {MessageBox.Show(this,"已有计时正在进行或暂停，请先结束它，再开始新计时。","已有计时");return;}
   pet.State.Sleeping=false;pet.State.StartFocus(n,rest?"休息":"专注",DateTime.UtcNow);pet.Say(rest?"给眼睛和肩膀放个小假吧。":"这一小段时间，我陪你一起。",6);pet.Save();Refresh();
  }
  public void SelectTab(int index) {if(Tabs!=null&&index>=0&&index<Tabs.Items.Count)Tabs.SelectedIndex=index;}
  void AppendLog(string who,string text) {
   if(chatLog==null)return;
   if(chatLog.Text.Length>9000)chatLog.Text=chatLog.Text.Substring(chatLog.Text.Length-7000);
   if(chatLog.Text.Length>0)chatLog.AppendText("\r\n");
   chatLog.AppendText(who+"："+text);
   chatLog.ScrollToEnd();
  }
  static string ClipText(string text,int max) {text=(text??"").Trim();return text.Length<=max?text:text.Substring(0,max)+"…";}
  static int SecondsFor(string text) {return Math.Max(8,Math.Min(30,(text??"").Length/4));}
  void RefreshKeyHint() {
   if(keyHint==null)return;
   string origin;string key=DeepSeek.ResolveKey(pet.State,out origin);
   keyHint.Text=DeepSeek.ValidKey(key)?"当前密钥："+DeepSeek.MaskKey(key)+"（来源："+origin+"）":"还没有配置密钥。密钥只保存在本机设置文件。";
  }
  void RefreshAiStatus(bool force) {
   if(aiStatus==null)return;
   if(chatBusy||pet.AiBusy) {if(aiStatus.Text!="◌ 正在接通 DeepSeek…")aiStatus.Text="◌ 正在接通 DeepSeek…";return;}
   if(!force&&DateTime.UtcNow<nextAiStatusCheck)return;
   nextAiStatusCheck=DateTime.UtcNow.AddSeconds(2);
   string origin;string key=DeepSeek.ResolveKey(pet.State,out origin);
   string status;
   if(!pet.State.AiChat)status="○ 在线 AI 已关闭 · 使用离线对白";
   else if(!DeepSeek.ValidKey(key))status="○ 尚未配置 API Key · 在「设置」里接入 DeepSeek";
   else if(origin=="设置文件")status="● 已接入 DeepSeek V4.1 Flash · 密钥保存在本机";
   else if(origin=="环境变量 DEEPSEEK_API_KEY")status="● 已接入 DeepSeek V4.1 Flash · 密钥来自环境变量";
   else status="● 已接入 DeepSeek V4.1 Flash · 密钥来自本地文件";
   if(force||status!=lastAiStatus) {lastAiStatus=status;aiStatus.Text=status;}
  }
  void SendChat() {
   if(chatBusy||pet.AiBusy)return;
   string text=(chat.Text??"").Trim();
   if(text.Length==0)return;
   if(text.Length>4000)text=text.Substring(0,4000);
   chat.Clear();
   if(pet.State.Sleeping) {pet.State.Sleeping=false;pet.Save();}
   AppendLog("你",text);
   string origin;string key=DeepSeek.ResolveKey(pet.State,out origin);
   if(!pet.State.AiChat) {OfflineReply(text,"在线 AI 已关闭，这次用离线对白陪你。");return;}
   if(!DeepSeek.ValidKey(key)) {OfflineReply(text,"还没有配置 API Key。在「设置」里接入 DeepSeek，就能在线聊天。");return;}
   ChatTurn[] turns=pet.Chat.Build(pet.State,text);
   string baseUrl=pet.State.ApiBase,model=pet.State.ApiModel;bool thinking=pet.State.AiThinking;
   chatBusy=true;pet.AiBusy=true;sendButton.IsEnabled=false;chat.IsEnabled=false;
   reply.Text="正在接通 DeepSeek…";RefreshAiStatus(true);
   pet.Act(6,"正在接通 DeepSeek，稍等一下…",20);
   ThreadPool.QueueUserWorkItem(delegate {
    ChatOutcome outcome=DeepSeek.Complete(key,baseUrl,model,thinking,turns,45);
    try {pet.Dispatcher.BeginInvoke(new Action(delegate {FinishChat(text,outcome);}));} catch {}
   });
  }
  void FinishChat(string user,ChatOutcome outcome) {
   chatBusy=false;pet.AiBusy=false;sendButton.IsEnabled=true;chat.IsEnabled=true;
   if(outcome.Ok) {
    pet.Chat.Remember(user,outcome.Text);
    AppendLog("曳尔沐",outcome.Text);
    reply.Text="DeepSeek V4.1 Flash · "+outcome.Seconds.ToString("0.0")+"s · "+outcome.CompletionTokens+" tokens";
    pet.Act(6,ClipText(outcome.Text,120),SecondsFor(outcome.Text));
   } else {
    OfflineReply(user,outcome.Error);
   }
   if(IsVisible)chat.Focus();
   RefreshAiStatus(true);
  }
  void OfflineReply(string text,string hint) {
   string local=Dialogue.Reply(text);
   pet.Chat.Remember(text,local);
   AppendLog("曳尔沐（离线）",local);
   reply.Text=hint;
   pet.Act(6,ClipText(local,120),SecondsFor(local));
  }
  void ClearChat() {
   if(pet.AiBusy) {reply.Text="等这次回复回来，再清空对话吧。";return;}
   pet.Chat.Clear();chatLog.Clear();reply.Text="对话记忆已经清空。";if(IsVisible)chat.Focus();
  }
  void SaveAiSettings() {
   if(!DeepSeek.ValidBaseUrl(apiBase.Text)) {aiHint.Text="接口地址不完整，请填写 https://api.deepseek.com 这样的地址。";return;}
   string baseUrl=DeepSeek.NormalizeBaseUrl(apiBase.Text);
   string model=(apiModel.Text??"").Trim();
   if(model.Length==0)model=DeepSeek.DefaultModel;
   if(model.Length>80)model=model.Substring(0,80);
   string pasted=DeepSeek.CleanKey(apiKeyBox.Password);
   if(pasted.Length>0&&!DeepSeek.ValidKey(pasted)) {aiHint.Text="这个密钥看起来不完整，请重新复制完整的 API Key。";return;}
   bool keyChanged=pasted.Length>0;
   if(keyChanged)pet.State.ApiKey=pasted;
   pet.State.ApiBase=baseUrl;pet.State.ApiModel=model;pet.State.AiThinking=aiThinkingBox.IsChecked==true;
   pet.Save();if(keyChanged&&Storage.LastError.Length==0)Storage.ForgetBackups();
   apiBase.Text=baseUrl;apiModel.Text=model;apiKeyBox.Clear();
   RefreshKeyHint();RefreshAiStatus(true);
   aiHint.Text=Storage.LastError.Length>0?"保存失败："+Storage.LastError:"AI 设置已保存。可以在「陪伴」页直接聊天。";
  }
  void ClearApiKey() {
   if(MessageBox.Show(this,"清除后，本机设置文件里的密钥会被删除。\n如果本地还有 api-key.txt 或环境变量，仍会继续使用。\n确定清除吗？","清除 API Key",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   pet.State.ApiKey="";pet.Save();if(Storage.LastError.Length==0)Storage.ForgetBackups();
   apiKeyBox.Clear();RefreshKeyHint();RefreshAiStatus(true);
   aiHint.Text="本机设置文件里的密钥已清除。";
  }
  void TestApi() {
   if(testBusy)return;
   string origin;string key=DeepSeek.CleanKey(apiKeyBox.Password);
   if(!DeepSeek.ValidKey(key))key=DeepSeek.ResolveKey(pet.State,out origin);
   if(!DeepSeek.ValidKey(key)) {aiHint.Text="先填入并保存 API Key，再测试连接。";return;}
   string baseUrl=apiBase.Text,model=(apiModel.Text??"").Trim();
   if(model.Length==0)model=DeepSeek.DefaultModel;
   testBusy=true;testButton.IsEnabled=false;aiHint.Text="正在测试连接（最多 25 秒）…";
   ThreadPool.QueueUserWorkItem(delegate {
    ChatOutcome outcome=DeepSeek.Complete(key,baseUrl,model,false,DeepSeek.ConnectivityTurns(),25);
    try {pet.Dispatcher.BeginInvoke(new Action(delegate {
     testBusy=false;testButton.IsEnabled=true;
     if(outcome.Ok)aiHint.Text="● 连接正常 · "+model+" · "+outcome.Seconds.ToString("0.0")+"s · 回复："+ClipText(outcome.Text,40);
     else aiHint.Text="○ 连接失败 · "+outcome.Error;
    }));} catch {}
   });
  }
  public void Refresh() {
   if(!IsVisible&&!pet.IsPreview)return;
   connection.Text=pet.State.Sleeping?"☾  休息中 · 连接仍在":"●  在线 · 信号满格";
   energy.Text="能量缓存  "+((int)pet.State.Energy)+"%";friendship.Text="相处印记  "+pet.State.Friendship;focusCount.Text="完成专注  "+pet.State.FocusSessions+" 段";
   double remaining=pet.State.Remaining(DateTime.UtcNow);focusClock.Text=PetWindow.FormatTime(remaining>0?remaining:pet.State.FocusMinutes*60);
   pause.Content=pet.State.PausedSeconds>0?"继续计时":"暂停计时";pause.IsEnabled=remaining>0;
   if(chatBusy||pet.AiBusy) {if(sendButton.IsEnabled){sendButton.IsEnabled=false;chat.IsEnabled=false;}}
   else if(!sendButton.IsEnabled){sendButton.IsEnabled=true;chat.IsEnabled=true;}
   RefreshAiStatus(false);
  }
 }
}
