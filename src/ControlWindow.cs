using System;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ElymsPet {
 public sealed class ControlWindow : Window {
  readonly PetWindow pet;
  readonly TextBlock connection,energy,friendship,focusClock,focusCount,reply,saveStatus;
  readonly TextBox note,chat,minutes;
  readonly Button pause;
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
   chat=Input("",44);chat.ToolTip="试试：你好 / 累了 / 学习 / 晚安 / 身份";
   reply=Text("在这里哦。想聊些什么？",14,"#C6DDD9");
   Action send=delegate {if(string.IsNullOrWhiteSpace(chat.Text))return;reply.Text=Dialogue.Reply(chat.Text);pet.Act(6,reply.Text,8);chat.Clear();};
   chat.KeyDown+=delegate(object o,System.Windows.Input.KeyEventArgs e) {if(e.Key==System.Windows.Input.Key.Enter){send();e.Handled=true;}};
   home.Children.Add(Card(Stack(Text("桌面小电台",18,"#E7EDFC"),Text("离线关键词互动 · 同人原创对白\n试试「你好」「累了」「学习」「晚安」「身份」。",12,"#A6B5CF"),chat,Button("发送给曳尔沐",send,true),reply)));
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
   AddTab(tabs,"设置",Card(settings));
   AddTab(tabs,"档案",Card(Stack(Text("曳尔沐 ≠ Y",22,"#BFCEF9"),Text("本桌宠呈现曳尔沐：E.T.D 六队副队长，擅长通讯、回应和电子世界中的往来。",14,"#DCE5F7"),Text("身份辨析\n借用形象不代表共享身份。档案专门提醒：使用小 Y 的样子，不能推导出 Y 是她的童年。这里的对白不会把 Y 的生平、与 Exe 的往事写成曳尔沐的亲历。",13,"#A9BBD8"),Text("外观依据\n当前银白长发、粉蓝渐变瞳、蝴蝶发夹、蓝色颈后蝴蝶结与白蓝副官制服。桌宠为 Q 版同人绘制。",13,"#A9BBD8"),Text("资料范围\n已核查官方版本公告、角色演示与社区档案转录，资料整理日期：2026-11-09。详细来源、技能概览和不确定项见随附资料。",12,"#91A4C4"),Row(Button("角色档案来源",delegate {Process.Start("https://www.ntestation.com/曳尔沐");},true),Button("你提供的参考视频",delegate {Process.Start("https://www.bilibili.com/video/BV11y4v6VEn5/");},false)),Text("同人作品 v1.0 · 图片由 imagegen 生成\n原创桌宠对白，不是游戏原声或联网 AI。",11,"#8498BA"))));
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
  public void Refresh() {
   if(!IsVisible&&!pet.IsPreview)return;
   connection.Text=pet.State.Sleeping?"☾  休息中 · 连接仍在":"●  在线 · 信号满格";
   energy.Text="能量缓存  "+((int)pet.State.Energy)+"%";friendship.Text="相处印记  "+pet.State.Friendship;focusCount.Text="完成专注  "+pet.State.FocusSessions+" 段";
   double remaining=pet.State.Remaining(DateTime.UtcNow);focusClock.Text=PetWindow.FormatTime(remaining>0?remaining:pet.State.FocusMinutes*60);
   pause.Content=pet.State.PausedSeconds>0?"继续计时":"暂停计时";pause.IsEnabled=remaining>0;
  }
 }
}
