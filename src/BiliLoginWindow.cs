using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ElymsPet {
 /// <summary>B 站扫码登录窗口：二维码只在本机生成，登录 Cookie 用 DPAPI 保存。</summary>
 public sealed class BiliLoginWindow : Window {
  readonly PetWindow pet;
  readonly WorkScope work;
  readonly Action changed;
  readonly Image qr;
  readonly TextBlock status,hint,account;
  readonly Button refresh,logout;
  readonly DispatcherTimer timer,closeTimer;
  BiliQrSession session;
  bool polling,closed;
  public BiliLoginWindow(PetWindow target,Action onChanged) {
   pet=target;
   work=new WorkScope(new DispatcherWorkScheduler(pet.Dispatcher),delegate(string error) {polling=false;status.Text=error;refresh.IsEnabled=true;},!pet.IsPreview);changed=onChanged;
   Title="B站 · 扫码登录";Width=390;Height=560;MinWidth=390;MinHeight=560;
   WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;
   Background=ControlWindow.Brush("#FFF7F0");Foreground=ControlWindow.Brush("#4F3B43");FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;
   Owner=pet.Panel==null?(Window)pet:pet.Panel;
   var root=new StackPanel{Margin=new Thickness(24,22,24,20)};
   Content=root;
   var title=ControlWindow.Text("B站 · 扫码登录",20,"#4F3B43");title.Margin=new Thickness(0,0,0,4);root.Children.Add(title);
   root.Children.Add(ControlWindow.Text("用手机 B 站扫下面的二维码。桌宠不会替你输入密码，登录 Cookie 只用 Windows DPAPI 加密保存在本机。",11,"#78616A"));
   qr=new Image{Width=260,Height=260,Stretch=Stretch.Uniform,Margin=new Thickness(0,10,0,8),HorizontalAlignment=HorizontalAlignment.Center};
   root.Children.Add(qr);
   status=ControlWindow.Text("正在准备二维码…",12,"#AD506B");status.Margin=new Thickness(0,2,0,6);root.Children.Add(status);
   account=ControlWindow.Text("",11,"#795F69");root.Children.Add(account);
   hint=ControlWindow.Text("扫码后在手机上确认；二维码 180 秒内有效。只保存登录态，不保存密码。",10.5,"#795F69");root.Children.Add(hint);
   refresh=ControlWindow.Button("刷新二维码",delegate {Start();},true);
   logout=ControlWindow.Button("退出登录",delegate {Logout();},false);
   root.Children.Add(Ui.Row(refresh,logout));
   timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(2)};
   timer.Tick+=delegate {Poll();};
   closeTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(1100)};
   closeTimer.Tick+=delegate {closeTimer.Stop();if(!closed)Close();};
   Loaded+=delegate {RefreshAccount();if(!BiliAuth.HasLogin)Start();};
   Closing+=delegate {work.Dispose();closed=true;timer.Stop();closeTimer.Stop();};
  }
  void RefreshAccount() {
   if(BiliAuth.HasLogin) {
    qr.Visibility=Visibility.Collapsed;
    refresh.Visibility=Visibility.Collapsed;
    logout.Visibility=Visibility.Visible;
    status.Text="● 已登录"+(string.IsNullOrEmpty(BiliAuth.Current.Name)?"":"："+BiliAuth.Current.Name);
    account.Text="登录态已保存在本机；可以关闭此窗口回到 B 站页。";
    hint.Text="退出登录会删除本机 Cookie；之后仍可匿名只读浏览。";
   } else {
    qr.Visibility=Visibility.Visible;
    refresh.Visibility=Visibility.Visible;
    logout.Visibility=Visibility.Collapsed;
   }
  }
  void Start() {
   if(closed)return;
   work.CancelPending();polling=false;timer.Stop();session=null;qr.Source=null;
   status.Text="正在准备二维码…";account.Text="";
   refresh.IsEnabled=false;
   work.Run(delegate {return BiliAuth.StartQr(20);},delegate(BiliQrSession value) {
    if(closed)return;
    refresh.IsEnabled=true;
    if(value==null||!value.Ok) {status.Text="○ "+(value==null?"没有拿到二维码。":value.Error);return;}
    session=value;qr.Source=value.Image;qr.Visibility=Visibility.Visible;
    status.Text="等待扫码…";account.Text="";
    timer.Start();
   });
  }
  void Poll() {
   if(polling||closed||session==null)return;
   polling=true;
   string key=session.Key;
   work.Run(delegate {return BiliAuth.Poll(key,20);},delegate(BiliLoginPoll result) {
    polling=false;
    if(closed)return;
    if(result==null) {status.Text="○ 扫码状态没有返回。";return;}
    if(!string.IsNullOrEmpty(result.Error)) {status.Text="○ "+result.Error;timer.Stop();return;}
    if(result.Status=="pending") {status.Text="等待扫码…";return;}
    if(result.Status=="scanned") {status.Text="已扫码，请在手机上确认。";return;}
    if(result.Status=="expired") {status.Text="二维码过期了，点「刷新二维码」再来一次。";timer.Stop();return;}
    if(result.Status=="confirmed") {
     timer.Stop();
     status.Text="● 登录成功"+(result.Account==null||string.IsNullOrEmpty(result.Account.Name)?"":"："+result.Account.Name);
     account.Text="之后可以在视频下方起草评论，确认后再发送。";
     qr.Visibility=Visibility.Collapsed;refresh.Visibility=Visibility.Collapsed;logout.Visibility=Visibility.Visible;
     if(changed!=null)changed();
     closeTimer.Start();
    }
   });
  }
  void Logout() {
   if(PopupDialog.Show(this,"退出登录会删除本机保存的 B站 Cookie。\n确定退出吗？","退出 B站登录",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   BiliAuth.Logout();
   if(changed!=null)changed();
   RefreshAccount();
   Start();
  }
 }
}
