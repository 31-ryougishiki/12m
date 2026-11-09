using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ElymsPet {
 /// <summary>「B站」页：让曳尔沐自己选题、搜索、读评论，再写一段逛后感。</summary>
 public sealed class BiliPanel {
  readonly PetWindow pet;
  TextBox topic,summary,commentBox;
  TextBlock status,summaryTitle,note,commentTitle,account,commentStatus;
  Button browse,login,logout,draft,send,deleteComment;
  StackPanel videos,comments;
  BiliVideo commentVideo,lastCommentVideo;
  long lastRpid;
  bool closed;
  public UIElement Root {get;private set;}
  public BiliPanel(PetWindow target) {
   pet=target;
   Root=Build();
   pet.Bili.Changed+=Refresh;
   pet.Bili.Finished+=OnFinished;
   Refresh();
  }
  UIElement Build() {
   status=Ui.Text("",12,"#9FE5D7");
   topic=Ui.Input("",42);topic.MaxLength=40;
   topic.ToolTip="留空就让她自己选题；也可以写一个想让她逛的方向。";
   topic.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    if(e.Key==Key.ImeProcessed)return;
    if(e.Key==Key.Enter||e.Key==Key.Return) {StartBrowse();e.Handled=true;}
   };
   browse=Ui.Button("让她去逛逛",StartBrowse,true);
   account=Ui.Text("",12,"#9FE5D7");
   login=Ui.Button("扫码登录",OpenLogin,false);
   logout=Ui.Button("退出登录",Logout,false);
   var head=Ui.Stack(
    Ui.Text("B站 · 让她自己逛逛",18,"#E7EDFC"),
    Ui.Text("留空时，她会根据你的存档画像自己挑一个方向；搜索公开视频，读标题、简介和热门评论，再写一段逛后感。默认匿名只读；扫码登录后可以起草评论，但发送前必须由你确认。",12,"#A6B5CF"),
    status,account,Ui.Row(login,logout),
    Ui.Text("想让她逛的方向（可留空）",12,"#A6B5CF"),
    topic,
    Ui.Row(browse),
    Ui.Text("B站接口可能触发风控；失败时她会改看热门。不点赞、不投币、不收藏、不关注，也不会自动发评论。",11,"#8FA4C7"));
   summaryTitle=Ui.Text("还没有逛过",18,"#E7EDFC");
   summary=Ui.Input("",156);summary.IsReadOnly=true;summary.AcceptsReturn=true;summary.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;summary.VerticalContentAlignment=VerticalAlignment.Top;summary.FontSize=12.5;
   summary.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   note=Ui.Text("",11,"#8FA4C7");
   videos=new StackPanel();
   commentTitle=Ui.Text("",15,"#E7EDFC");
   comments=new StackPanel();
   var result=Ui.Stack(summaryTitle,summary,note,Ui.Text("这次刷到的视频",14,"#E7EDFC"),videos,commentTitle,comments);
   commentBox=Ui.Input("",82);commentBox.MaxLength=1000;commentBox.AcceptsReturn=true;commentBox.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;commentBox.VerticalContentAlignment=VerticalAlignment.Top;
   commentBox.TextChanged+=delegate {UpdateCommentButtons();};
   draft=Ui.Button("让曳尔沐写一条",DraftComment,false);
   send=Ui.Button("发送评论",SendComment,true);
   deleteComment=Ui.Button("让她删掉刚发的评论",DeleteComment,false);
   deleteComment.Visibility=Visibility.Collapsed;
   commentStatus=Ui.Text("",11,"#8FA4C7");
   var editor=Ui.Stack(
    Ui.Text("评论 · 需要你确认",15,"#E7EDFC"),
    Ui.Text("她可以写草稿；只有你点「发送评论」并确认后，才会以你的账号提交。每条视频最多发一条，发送后 10 秒内不能连发。发送成功后，可以让她删掉刚发的这一条。",11,"#8FA4C7"),
    commentBox,Ui.Row(draft,send,deleteComment),commentStatus);
   var root=new StackPanel();
   root.Children.Add(Ui.Card(head));
   root.Children.Add(Ui.Card(result));
   root.Children.Add(Ui.Card(editor));
   return root;
  }
  void StartBrowse() {
   if(pet.Bili.Busy) {status.Text="她还在逛，等这一趟回来吧。";return;}
   if(!pet.Bili.Browse(topic.Text,false)) {status.Text="刚才那一趟还没结束，稍等一下。";return;}
   status.Text="◌ 正在 B 站逛…";
  }
  void Refresh() {
   if(status==null)return;
   status.Text=pet.Bili.StatusLine;
   browse.IsEnabled=!pet.Bili.Busy;
   bool logged=BiliAuth.HasLogin;
   account.Text=logged?(string.IsNullOrEmpty(BiliAuth.Current.Name)?"● 已登录":"● 已登录："+BiliAuth.Current.Name):"○ 未登录 · 匿名只读";
   login.Visibility=logged?Visibility.Collapsed:Visibility.Visible;
   logout.Visibility=logged?Visibility.Visible:Visibility.Collapsed;
   commentBox.IsEnabled=true;
   UpdateCommentButtons();
   BiliBrowseResult result=pet.Bili.Current;
   if(pet.Bili.Busy) {
    summaryTitle.Text="正在 B 站逛…";
    summary.Text="她正在自己选题、搜索、读简介和评论…";
    note.Text="";
    videos.Children.Clear();
    videos.Children.Add(Ui.Text("等她把这一趟逛完。",11,"#8FA4C7"));
    commentTitle.Text="";
    comments.Children.Clear();
    commentVideo=null;
    UpdateCommentButtons();
    return;
   }
   if(result==null) {
    summaryTitle.Text="还没有逛过";
    summary.Text="点上面的按钮，让她自己挑一个方向去 B 站逛逛。";
    note.Text="";
    videos.Children.Clear();
    videos.Children.Add(Ui.Text("这里会出现她刷到的视频。",11,"#8FA4C7"));
    commentTitle.Text="";
    comments.Children.Clear();
    commentVideo=null;
    UpdateCommentButtons();
    return;
   }
   Render(result);
  }
  void OnFinished(BiliBrowseResult result) {if(closed)return;Refresh();}
  void Render(BiliBrowseResult result) {
   if(!result.Ok) {
    summaryTitle.Text="这次没有逛成";
    summary.Text=result.Error;
    note.Text=result.Note;
    videos.Children.Clear();
    commentTitle.Text="";
    comments.Children.Clear();
    commentVideo=null;
    UpdateCommentButtons();
    return;
   }
   summaryTitle.Text="逛后感 · "+BiliService.Clip(result.Topic,32);
   summary.Text=result.Summary;
   note.Text=result.Note;
   commentVideo=result.Selected;
   UpdateCommentButtons();
   videos.Children.Clear();
   BiliVideo[] list=result.Videos??new BiliVideo[0];
   if(list.Length==0) videos.Children.Add(Ui.Text("这次没有刷到视频。",11,"#8FA4C7"));
   for(int i=0;i<list.Length&&i<5;i++) videos.Children.Add(VideoCard(list[i],result.Selected));
   comments.Children.Clear();
   if(result.Selected!=null) commentTitle.Text="评论区 · "+BiliService.Clip(result.Selected.Title,36);
   else commentTitle.Text="";
   BiliComment[] cs=result.Comments??new BiliComment[0];
   if(cs.Length==0) comments.Children.Add(Ui.Text("评论没有取到，或者这个视频还没有热门评论。",11,"#8FA4C7"));
   for(int i=0;i<cs.Length&&i<4;i++) {
    BiliComment c=cs[i];
    var line=Ui.Text("· "+BiliService.Clip(c.Author,20)+"（"+BiliApi.FormatCount(c.Like)+"赞）\n"+BiliService.Clip(c.Message,200),11,"#B8C8E5");
    line.LineHeight=17;line.Margin=new Thickness(0,0,0,9);
    comments.Children.Add(line);
   }
  }
  void UpdateCommentButtons() {
   if(draft==null)return;
   bool has=commentVideo!=null;
   draft.IsEnabled=has;
   send.IsEnabled=has&&BiliAuth.HasLogin&&(commentBox.Text??"").Trim().Length>0;
  }
  void OpenLogin() {
   var window=new BiliLoginWindow(pet,delegate {Refresh();});
   window.ShowDialog();
   Refresh();
  }
  void Logout() {
   if(MessageBox.Show(pet.Panel==null?(Window)pet:pet.Panel,"退出登录会删除本机保存的 B站 Cookie。\n确定退出吗？","退出 B站登录",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   BiliAuth.Logout();
   commentStatus.Text="已退出登录；仍可匿名只读浏览。";
   Refresh();
  }
  void DraftComment() {
   if(commentVideo==null) {commentStatus.Text="先让她逛到一条视频，再写评论。";return;}
   draft.IsEnabled=false;
   commentStatus.Text="◌ 正在按视频内容写评论草稿…";
   pet.Bili.DraftComment(commentVideo,delegate(string text) {
    draft.IsEnabled=true;
    if(string.IsNullOrEmpty(text)) {commentStatus.Text="这次没有写出草稿；检查 API Key 或稍后再试。";return;}
    commentBox.Text=text;
    commentStatus.Text="草稿已写好；可以修改，确认后再发送。";
    UpdateCommentButtons();
   });
  }
  void SendComment() {
   if(commentVideo==null) {commentStatus.Text="没有选中视频。";return;}
   if(!BiliAuth.HasLogin) {commentStatus.Text="发送评论需要先扫码登录。";OpenLogin();return;}
   string text=(commentBox.Text??"").Trim();
   if(text.Length==0) {commentStatus.Text="评论内容不能为空。";return;}
   if(text.Length>1000) {commentStatus.Text="评论最多 1000 字。";return;}
   string name=string.IsNullOrEmpty(BiliAuth.Current.Name)?"你的 B站账号":BiliAuth.Current.Name;
   string message="将以「"+name+"」的身份，在《"+BiliService.Clip(commentVideo.Title,50)+"》发送评论：\n\n"+BiliService.Clip(text,300)+"\n\n评论会公开发布。确定发送吗？";
   Window owner=pet.Panel==null?(Window)pet:pet.Panel;
   if(MessageBox.Show(owner,message,"发送 B站评论",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   send.IsEnabled=false;draft.IsEnabled=false;commentStatus.Text="◌ 正在发送评论…";
   pet.Bili.SendComment(commentVideo,text,delegate(BiliCommentResult result) {
    send.IsEnabled=true;draft.IsEnabled=true;
    if(result==null||!result.Ok) {commentStatus.Text="○ 评论没有发出去："+(result==null?"没有返回结果":result.Error);return;}
    lastRpid=result.Rpid;lastCommentVideo=commentVideo;deleteComment.Visibility=Visibility.Visible;
    commentStatus.Text="● 评论已发送"+(result.Rpid>0?" · rpid "+result.Rpid:"")+"; 公开发布，可在 B站 删除。";
    commentBox.Clear();
    UpdateCommentButtons();
   });
  }
  void DeleteComment() {
   if(lastRpid<=0||lastCommentVideo==null) {commentStatus.Text="没有刚发出去、可以删除的评论。";return;}
   if(!BiliAuth.HasLogin) {commentStatus.Text="删除评论需要先登录。";return;}
   string message="让曳尔沐删除她刚发的这条评论吗？\n\nrpid "+lastRpid+"\n《"+BiliService.Clip(lastCommentVideo.Title,50)+"》\n\n删除后不能恢复。";
   Window owner=pet.Panel==null?(Window)pet:pet.Panel;
   if(MessageBox.Show(owner,message,"删除 B站评论",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   deleteComment.IsEnabled=false;commentStatus.Text="◌ 正在删除评论…";
   BiliVideo video=lastCommentVideo;long rpid=lastRpid;
   pet.Bili.DeleteComment(video,rpid,delegate(BiliCommentResult result) {
    deleteComment.IsEnabled=true;
    if(result==null||!result.Ok) {commentStatus.Text="○ 评论没有删掉："+(result==null?"没有返回结果":result.Error);return;}
    lastRpid=0;lastCommentVideo=null;deleteComment.Visibility=Visibility.Collapsed;
    commentStatus.Text="● 已删除评论 · rpid "+rpid+"。";
   });
  }
  UIElement VideoCard(BiliVideo video,BiliVideo selected) {
   var card=new Border{Background=Ui.Brush("#111D31"),BorderBrush=Ui.Brush("#2B3B58"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(14),Margin=new Thickness(0,0,0,11)};
   var panel=new StackPanel();
   bool current=selected!=null&&selected.Bvid==video.Bvid;
   var title=Ui.Text((current?"她停在这里 · ":"")+video.Title,13.5,current?"#AFEDE1":"#E7EDFC");
   title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,5);
   panel.Children.Add(title);
   string meta=(current?"当前 · ":"")+BiliService.Clip(video.Author,24)+" · "+video.PlayText+"播放";
   if(video.Danmaku>0) meta+=" · "+BiliApi.FormatCount(video.Danmaku)+"弹幕";
   if(video.DateText.Length>0) meta+=" · "+video.DateText;
   panel.Children.Add(Ui.Text(meta,10.5,"#8FA4C7"));
   if(video.Description.Length>0) {
    var desc=Ui.Text(BiliService.Clip(video.Description,150),11.5,"#B8C8E5");desc.LineHeight=18;desc.Margin=new Thickness(0,5,0,5);
    panel.Children.Add(desc);
   }
   string link=video.Url;
   panel.Children.Add(Ui.Row(Ui.Button("打开B站",delegate {Open(link);},false),Ui.Button("复制链接",delegate {Copy(link);},false)));
   card.Child=panel;
   return card;
  }
  void Open(string url) {try {Process.Start(url);} catch {}}
  void Copy(string url) {try {Clipboard.SetText(url);status.Text="链接已复制。";} catch(Exception e) {status.Text="复制失败："+e.Message;}}
  public void RefreshTab() {Refresh();}
  public void Dispose() {closed=true;pet.Bili.Changed-=Refresh;pet.Bili.Finished-=OnFinished;}
 }
}
