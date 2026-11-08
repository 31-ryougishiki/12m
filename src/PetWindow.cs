using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace ElymsPet {
 public sealed class Sprite : FrameworkElement {
  public BitmapSource[] Frames;
  byte[][] alpha;
  int fw,fh;
  public int Frame;
  public double Bounce, Tilt;
  public bool Flip;
  public Sprite() {
   using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("sprites.png")) {
    var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze();
    fw=bitmap.PixelWidth/3; fh=bitmap.PixelHeight/3; Frames=new BitmapSource[9]; alpha=new byte[9][];
    for(int i=0;i<9;i++) { var c=new CroppedBitmap(bitmap,new Int32Rect((i%3)*fw,(i/3)*fh,fw,fh)); c.Freeze(); Frames[i]=c;
     var bgra=new FormatConvertedBitmap(c,PixelFormats.Bgra32,null,0); alpha[i]=new byte[fw*fh*4]; bgra.CopyPixels(alpha[i],fw*4,0);
    }
   }
   Cursor=Cursors.Hand;
  }
  protected override void OnRender(DrawingContext dc) {
   dc.PushTransform(new TranslateTransform(0,Bounce));
   dc.PushTransform(new RotateTransform(Tilt,ActualWidth/2,ActualHeight*.8));
   dc.PushTransform(new ScaleTransform(Flip?-1:1,1,ActualWidth/2,ActualHeight/2));
   dc.DrawImage(Frames[Frame],new Rect(0,0,ActualWidth,ActualHeight)); dc.Pop();dc.Pop();dc.Pop();
  }
  protected override HitTestResult HitTestCore(PointHitTestParameters p) {
   double dx=p.HitPoint.X,dy=p.HitPoint.Y-Bounce;
   if(Flip) dx=ActualWidth-dx;
   int x=(int)(dx/ActualWidth*fw),y=(int)(dy/ActualHeight*fh);
   if(x<0||y<0||x>=fw||y>=fh) return null;
   return alpha[Frame][(y*fw+x)*4+3]>20 ? new PointHitTestResult(this,p.HitPoint) : null;
  }
 }
 public sealed class PetWindow : Window {
  public readonly PetState State;
  public Sprite Art;
  public ControlWindow Panel;
  public readonly ChatSession Chat=new ChatSession();
  public bool AiBusy;
  readonly Canvas canvas=new Canvas();
  readonly Border bubble=new Border();
  readonly TextBlock bubbleText=new TextBlock();
  readonly Border badge=new Border();
  readonly TextBlock badgeText=new TextBlock();
  readonly Random random=new Random();
  readonly DispatcherTimer timer=new DispatcherTimer();
  readonly System.Diagnostics.Stopwatch clock=System.Diagnostics.Stopwatch.StartNew();
  Forms.NotifyIcon tray;
  DateTime bubbleUntil=DateTime.MinValue,lastIdle=DateTime.UtcNow,lastSave=DateTime.UtcNow,lastWater=DateTime.UtcNow;
  double stateUntil,walkUntil,nextWalk=8,lastTick;
  int forced=-1,direction=1;
  bool dragging,pressed,moved,follow,passThrough,closing;
  bool fullscreen;
  int lastWindowCheck=-1;
  string foregroundTitle="";
  Point pressCursor,pressWindow;
  IntPtr handle;
  public bool IsPreview;
  const int GWL_EXSTYLE=-20,WS_EX_TRANSPARENT=0x20,WS_EX_TOOLWINDOW=0x80;
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int n);
  [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int n,int v);
  [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
  [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,System.Text.StringBuilder text,int max);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,System.Text.StringBuilder text,int max);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out NativeRect rect);
  [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
  public PetWindow(PetState state,bool preview) {
   State=state; IsPreview=preview; Title="曳尔沐 · 桌面来电"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
   AllowsTransparency=true; Background=Brushes.Transparent; ShowInTaskbar=false; ShowActivated=false; Topmost=state.Topmost;
   Content=canvas; Art=new Sprite(); canvas.Children.Add(Art);
   bubble.Background=ControlWindow.Brush("#F4F2FF"); bubble.BorderBrush=ControlWindow.Brush("#A9B7F5"); bubble.BorderThickness=new Thickness(1); bubble.CornerRadius=new CornerRadius(18,18,18,4); bubble.Padding=new Thickness(14,10,14,10);
   bubbleText.FontFamily=new FontFamily("Microsoft YaHei UI"); bubbleText.FontSize=12; bubbleText.LineHeight=21; bubbleText.TextWrapping=TextWrapping.Wrap; bubbleText.Foreground=ControlWindow.Brush("#293456"); bubble.Child=bubbleText; bubble.Visibility=Visibility.Collapsed; canvas.Children.Add(bubble);
   badge.Background=ControlWindow.Brush("#DF18263F"); badge.CornerRadius=new CornerRadius(13); badge.Padding=new Thickness(12,5,12,5);
   badgeText.FontFamily=new FontFamily("Microsoft YaHei UI"); badgeText.FontSize=11; badgeText.Foreground=ControlWindow.Brush("#C4F5EE"); badge.Child=badgeText; badge.Cursor=Cursors.Hand; badge.MouseLeftButtonUp+=delegate { OpenPanel(); }; canvas.Children.Add(badge);
   badgeText.Text="●  曳尔沐 · 信号满格";
   Art.MouseLeftButtonDown+=OnDown; Art.MouseMove+=OnMove; Art.MouseLeftButtonUp+=OnUp;
   Art.LostMouseCapture+=delegate { pressed=false; dragging=false; };
   MouseWheel+=delegate(object o,MouseWheelEventArgs e) { ResizePet(State.Scale+(e.Delta>0?.08:-.08)); e.Handled=true; };
   MouseRightButtonUp+=delegate(object o,MouseButtonEventArgs e) { var menu=BuildMenu(); menu.PlacementTarget=this; menu.IsOpen=true; e.Handled=true; };
   SourceInitialized+=delegate { handle=new WindowInteropHelper(this).Handle; SetWindowLong(handle,GWL_EXSTYLE,GetWindowLong(handle,GWL_EXSTYLE)|WS_EX_TOOLWINDOW);
    HwndSource.FromHwnd(handle).AddHook(Hook); if(!preview) HotkeyAvailable=RegisterHotKey(handle,1,0x4000|2|1,0x45); };
   Loaded+=delegate { ResizePet(State.Scale); if(State.X > -90000) { Left=State.X;Top=State.Y;Clamp(); } else Home();
    if(!preview) { InitTray(); timer.Start(); Say(State.Sleeping?"睡眠频道已开启。明天再连接。":"曳尔沐已连接。拖动我挪位置，右键看看有什么好玩的。",8); }
   };
   timer.Interval=TimeSpan.FromMilliseconds(40); timer.Tick+=Tick;
   Closing+=delegate { if(closing) return; closing=true; Save(); timer.Stop(); if(tray!=null) tray.Dispose(); if(handle!=IntPtr.Zero) UnregisterHotKey(handle,1); if(Panel!=null) Panel.Close(); };
  }
  public bool HotkeyAvailable;
  IntPtr Hook(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled) { if(msg==0x312&&w.ToInt32()==1) { Recover(); handled=true; } return IntPtr.Zero; }
  public Rect WorkArea() {
   var c=Forms.Cursor.Position;
   var point=new System.Drawing.Point((int)((Left+Width/2)*Dpi), (int)((Top+Height/2)*Dpi));
   if(double.IsNaN(Left)||Left < -9000) point=c;
   var r=Forms.Screen.FromPoint(point).WorkingArea; return new Rect(r.X/Dpi,r.Y/Dpi,r.Width/Dpi,r.Height/Dpi);
  }
  double Dpi { get { var s=PresentationSource.FromVisual(this);return s==null?1:s.CompositionTarget.TransformToDevice.M11; } }
  public void Clamp() { Rect r=WorkArea();Left=Math.Max(r.Left,Math.Min(r.Right-Width,Left));Top=Math.Max(r.Top,Math.Min(r.Bottom-Height,Top)); }
  public void Home() { var r=WorkArea();Left=r.Right-Width-35;Top=r.Bottom-Height-12;Clamp(); }
  public void ResizePet(double value) {
   State.Scale=Math.Max(.65,Math.Min(1.6,value)); double s=State.Scale;
   double oldH=Height; Width=300*s;Height=380*s; canvas.Width=Width;canvas.Height=Height;
   Art.Width=300*s;Art.Height=300*s; Canvas.SetTop(Art,66*s);
   bubble.Width=240*s; Canvas.SetLeft(bubble,30*s); Canvas.SetTop(bubble,2*s);
   Canvas.SetTop(badge,Height-27); Canvas.SetLeft(badge,Width/2-72);
   if(IsLoaded) {if(!double.IsNaN(oldH)) Top+=oldH-Height;Clamp();} Save();
  }
  Point CursorPoint() { var p=Forms.Cursor.Position; return new Point(p.X/Dpi,p.Y/Dpi); }
  void OnDown(object o,MouseButtonEventArgs e) {
   if(e.ClickCount==2) { pressed=false; OpenPanel();return; }
   pressed=true;moved=false;pressCursor=CursorPoint();pressWindow=new Point(Left,Top);Art.CaptureMouse();e.Handled=true;
  }
  void OnMove(object o,MouseEventArgs e) {
   if(!pressed) return;var p=CursorPoint();var d=p-pressCursor;
   if(d.Length>4) {moved=true;dragging=true;Left=pressWindow.X+d.X;Top=pressWindow.Y+d.Y;}
  }
  void OnUp(object o,MouseButtonEventArgs e) {
   if(!pressed) return; bool wasMoved=moved;pressed=false;dragging=false;Art.ReleaseMouseCapture();Clamp();
   if(wasMoved) { Act(4,"换个角度，也能发现新风景。",3);Save(); } else Pet();e.Handled=true;
  }
  public void Pet() { if(State.Sleeping) {Say("唔……让我再缓存一个小小的梦。",4); return;} State.Friendship++; Act(5,Dialogue.Touch[random.Next(Dialogue.Touch.Length)],4);Save(); }
  public void Feed() { State.Sleeping=false; State.Energy=Math.Min(100,State.Energy+15);State.Friendship+=2;Act(5,Dialogue.Food[random.Next(Dialogue.Food.Length)],5);Save(); }
  public void Call() { State.Sleeping=false; Act(6,"桌面频道已接通。今天有什么想和我分享的吗？",6); }
  public void SleepToggle() { State.Sleeping=!State.Sleeping; forced=-1;walkUntil=0; Say(State.Sleeping?"晚安。把没做完的事留给明天，连接不会因此消失。":"睡眠缓存读取完毕。又见面啦。",6);Save(); }
  public void Act(int frame,string text,int seconds) { forced=frame;stateUntil=clock.Elapsed.TotalSeconds+seconds;Say(text,seconds); }
  public void Say(string text,int seconds) { bubbleText.Text=text;bubbleUntil=DateTime.UtcNow.AddSeconds(seconds);bubble.Visibility=Visibility.Visible; }
  public void Save() { if(IsPreview) return; if(IsLoaded) {State.X=Left;State.Y=Top;} Storage.Save(State); }
  void Tick(object o,EventArgs e) {
   var now=DateTime.UtcNow;double t=clock.Elapsed.TotalSeconds,dt=Math.Min(.15,t-lastTick);lastTick=t;
   if(lastWindowCheck!=(int)t) {lastWindowCheck=(int)t;CheckForeground();}
   bool focus=State.FocusEnd!=DateTime.MinValue;
   if(State.CompleteIfDue(now)) { Act(4,State.FocusKind=="专注"?"这一段专注完成啦！离开屏幕，伸个懒腰吧。":"休息结束。准备好开启下一段了吗？",12); if(!State.Muted) System.Media.SystemSounds.Asterisk.Play();Save(); }
   if(now>bubbleUntil) bubble.Visibility=Visibility.Collapsed;
   if(t>stateUntil) forced=-1;
   bool activeMove=!fullscreen&&!State.Sleeping&&!dragging&&!pressed&&!focus&&forced<0;
   if(activeMove&&follow) { var p=CursorPoint(); double delta=p.X-(Left+Width/2); if(Math.Abs(delta)>80) {direction=delta>0?1:-1; Left+=direction*65*dt;walkUntil=t+.2;Clamp();} }
   else if(activeMove&&State.Wander) {
    if(t>nextWalk) {direction=random.Next(2)==0?-1:1;walkUntil=t+2+random.NextDouble()*3;nextWalk=walkUntil+8+random.NextDouble()*12;}
    if(t<walkUntil) { Left+=direction*30*dt; var r=WorkArea();if(Left<=r.Left||Left+Width>=r.Right) {direction*=-1;Clamp();} }
   }
   int frame=0;
   if(dragging) frame=8;else if(State.Sleeping) frame=7;else if(forced>=0) frame=forced;else if(activeMove&&t<walkUntil) frame=((int)(t*5)%2==0)?2:3;else if(t%4.6>4.4) frame=1;
   Art.Frame=frame;Art.Flip=(frame==2||frame==3)&&direction<0; Art.Bounce=(State.Sleeping?Math.Sin(t*1.4):Math.Sin(t*2.4))*2*State.Scale;
   Art.Tilt=dragging?Math.Sin(t*8)*5:(frame==2||frame==3?Math.Sin(t*10)*2:0);Art.InvalidateVisual();
   State.Energy=Math.Max(15,Math.Min(100,State.Energy+dt*(State.Sleeping?.08:-.004)));
   badgeText.Text=State.Sleeping?"☾  休息中 · 保持连接":focus?"◷  "+State.FocusKind+" "+FormatTime(State.Remaining(now)):"●  曳尔沐 · 信号满格";
   if(!fullscreen&&!State.Sleeping&&!focus&&(now-lastIdle).TotalSeconds>85) {lastIdle=now;Say(State.WindowComments&&random.Next(2)==0?WindowRemark():Dialogue.Idle[random.Next(Dialogue.Idle.Length)],6);}
   if(State.RemindWater&&!State.Sleeping&&(now-lastWater).TotalMinutes>=45) {lastWater=now;Say("补充一点水分，也让眼睛看看远处吧。",10);}
   if((now-lastSave).TotalSeconds>30) {lastSave=now;Save();}
   if(Panel!=null) Panel.Refresh();
  }
  public static string FormatTime(double seconds) { int n=Math.Max(0,(int)Math.Ceiling(seconds));return (n/60).ToString("00")+":"+(n%60).ToString("00"); }
  void CheckForeground() {
   fullscreen=false;var h=GetForegroundWindow();
   if(h!=IntPtr.Zero&&h!=handle&&(Panel==null||h!=new WindowInteropHelper(Panel).Handle)) {
    var c=new System.Text.StringBuilder(256);GetClassName(h,c,c.Capacity);string cls=c.ToString();
    if(cls!="WorkerW"&&cls!="Progman"&&cls!="Shell_TrayWnd") {
     NativeRect rect;var screen=Forms.Screen.FromHandle(h).Bounds;
     if(State.FullscreenQuiet&&GetWindowRect(h,out rect))fullscreen=rect.Left<=screen.Left&&rect.Top<=screen.Top&&rect.Right>=screen.Right&&rect.Bottom>=screen.Bottom;
     if(State.WindowComments) {var text=new System.Text.StringBuilder(512);GetWindowText(h,text,text.Capacity);foregroundTitle=text.ToString();}
    }
   }
   Opacity=fullscreen?0:1;
  }
  string WindowRemark() {
   string t=foregroundTitle.ToLowerInvariant();
   if(t.Contains("code")||t.Contains("studio")||t.Contains("idea"))return "看起来正在写代码呀。先解决眼前这一小块，我们慢慢来。";
   if(t.Contains("异环")||t.Contains("neverness"))return "又回到海特洛啦。玩得开心，也记得照顾一下眼睛。";
   if(t.Contains("bilibili")||t.Contains("哔哩")||t.Contains("youtube"))return "发现视频频道。快乐可以连播，休息也要排上号哦。";
   if(t.Contains("word")||t.Contains("excel")||t.Contains("文档"))return "这一页也会慢慢完成的。要不要先记下最重要的一件事？";
   if(t.Contains("音乐")||t.Contains("music"))return "给今天选一段背景音乐吧，安静的也很好听。";
   return "我在这个小窗口里，陪你处理眼前的事情。";
  }
  public void OpenPanel() {if(Panel==null) {Panel=new ControlWindow(this);Panel.Closed+=delegate {Panel=null;};} Panel.Show();Panel.Activate();}
  public void OpenPanel(int tab) {OpenPanel();if(Panel!=null)Panel.SelectTab(tab);}
  public void Recover() {SetPassThrough(false);Show();Home();Say("我在这里，已经重新连接啦。",5);}
  public void SetPassThrough(bool enabled) {passThrough=enabled;if(handle!=IntPtr.Zero) {int ex=GetWindowLong(handle,GWL_EXSTYLE);SetWindowLong(handle,GWL_EXSTYLE,enabled?ex|WS_EX_TRANSPARENT:ex&~WS_EX_TRANSPARENT);} }
  MenuItem Item(string title,Action a) { var item=new MenuItem {Header=title};item.Click+=delegate {a();};return item; }
  MenuItem Check(string title,bool value,Action<bool> set) {var item=new MenuItem{Header=title,IsCheckable=true,IsChecked=value};item.Click+=delegate {set(item.IsChecked);Save();};return item;}
  ContextMenu BuildMenu() {
   var m=new ContextMenu {FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=13};
   m.Items.Add(Item("打开陪伴面板",OpenPanel));m.Items.Add(Item("和曳尔沐聊聊",delegate {OpenPanel(0);}));m.Items.Add(Item("摸摸头",Pet));m.Items.Add(Item("投喂数据零食",Feed));m.Items.Add(Item("接通来电",Call));m.Items.Add(Item(State.Sleeping?"唤醒曳尔沐":"晚安，休息一会儿",SleepToggle));m.Items.Add(new Separator());
   m.Items.Add(Check("桌面散步",State.Wander,delegate(bool v) {State.Wander=v;follow=false;}));
   m.Items.Add(Check("跟随鼠标",follow,delegate(bool v) {follow=v;State.Wander=false;}));
   m.Items.Add(Check("始终置顶",State.Topmost,delegate(bool v) {State.Topmost=v;Topmost=v;}));
   m.Items.Add(Item("鼠标穿透（从托盘恢复）",delegate {Say("已进入安静陪伴。托盘菜单可恢复操作。",5);SetPassThrough(true);}));
   m.Items.Add(Item("恢复默认大小与位置",delegate {ResizePet(1);Home();}));m.Items.Add(new Separator());
   m.Items.Add(Item("隐藏到托盘",delegate {Hide();}));m.Items.Add(Item("退出",delegate {Close();}));return m;
  }
  void InitTray() {
   tray=new Forms.NotifyIcon();tray.Text="曳尔沐 · 桌面来电";
   using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("pet.ico")) {tray.Icon=new System.Drawing.Icon(s);}
   var m=new Forms.ContextMenuStrip();
   m.Items.Add("打开陪伴面板",null,delegate {Dispatcher.Invoke(new Action(OpenPanel));});
   m.Items.Add("和曳尔沐聊聊",null,delegate {Dispatcher.Invoke(new Action(delegate {OpenPanel(0);}));});
   m.Items.Add("召回 / 解除鼠标穿透",null,delegate {Dispatcher.Invoke(new Action(Recover));});
   m.Items.Add("睡眠 / 唤醒",null,delegate {Dispatcher.Invoke(new Action(SleepToggle));});
   m.Items.Add("隐藏 / 显示",null,delegate {Dispatcher.Invoke(new Action(delegate {if(IsVisible) Hide();else Show();}));});
   m.Items.Add("退出",null,delegate {Dispatcher.Invoke(new Action(Close));});tray.ContextMenuStrip=m;tray.Visible=true;
   tray.DoubleClick+=delegate {Dispatcher.Invoke(new Action(Recover));};
  }
 }
}
