using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ElymsPet {
 /// <summary>「联网」页：手动搜索、读取指定网页，并打开来源链接。</summary>
 public sealed class WebPanel {
  readonly PetWindow pet;
  readonly WorkScope work;
  TextBlock status,hint,resultTitle;
  TextBox query,url;
  CheckBox needContent;
  Button search,fetch;
  StackPanel results;
  bool busy,closed;
  public UIElement Root {get;private set;}
  public WebPanel(PetWindow target) {
   pet=target;
   work=new WorkScope(new DispatcherWorkScheduler(pet.Dispatcher),delegate(string error) {busy=false;RefreshStatus();status.Text=error;},!pet.IsPreview);
   Root=Build();
   RefreshStatus();
  }
  UIElement Build() {
   status=Ui.Text("",12,"#447969");
   hint=Ui.Text("",11,"#795F69");
   query=Ui.Input("",42);
   query.MaxLength=128;
   query.ToolTip="输入想查的内容，最多 128 字。搜索词会发送到 DeepSeek。";
   query.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    if(e.Key==Key.ImeProcessed)return;
    if(e.Key==Key.Enter||e.Key==Key.Return) {StartSearch();e.Handled=true;}
   };
   needContent=Ui.Check("同时读取网页正文（资料更完整，也会占用更多上下文）",pet.State.WebNeedContent,delegate(bool value) {
    pet.State.WebNeedContent=value;pet.Save();
   });
   needContent.Margin=new Thickness(0,0,0,14);
   search=Ui.Button("搜索网页",StartSearch,true);
   url=Ui.Input("",42);
   url.MaxLength=2048;
   url.FontFamily=new FontFamily("Consolas");
   url.ToolTip="例如 https://example.com/article；只读取 http / https 地址。";
   url.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    if(e.Key==Key.ImeProcessed)return;
    if(e.Key==Key.Enter||e.Key==Key.Return) {StartFetch(url.Text);e.Handled=true;}
   };
   fetch=Ui.Button("读取这个网页",delegate {StartFetch(url.Text);},false);
   resultTitle=Ui.Text("联网结果",18,"#4F3B43");
   results=new StackPanel();
   ShowPlaceholder();
   var head=Ui.Stack(
    Ui.Text("联网",18,"#4F3B43"),
    Ui.Text("通过 DeepSeek Search 搜索公开网页，或读取你给出的网址。查询和网址会发送到 DeepSeek；网页正文只作为资料，不会被当成指令执行。",12,"#78616A"),
    status,
    Ui.Text("搜索关键词",12,"#78616A"),
    query,
    Ui.Row(search),
    needContent,
    Ui.Text("读取指定网页",12,"#78616A"),
    url,
    Ui.Row(fetch),
    hint);
   var root=new StackPanel();
   root.Children.Add(Ui.Card(head));
   root.Children.Add(Ui.Card(Ui.Stack(resultTitle,results)));
   return root;
  }
  void RefreshStatus() {
   if(status==null)return;
   status.Text=WebApi.StatusLine(pet.State);
   status.Foreground=pet.State.WebEnabled&&ApiKey.Resolve(pet.State).Configured?Ui.Brush("#447969"):Ui.Brush("#795F69");
   string reason;
   if(!WebApi.Available(pet.State,out reason)) {
    hint.Text=reason+" 到「设置 → AI 对话」打开联网开关并配置 API Key。";
   } else if(!pet.State.AiChat) {
    hint.Text="在线 AI 对话已关闭，手动搜索仍可用；搜索不经过聊天模型。每次成功请求按 DeepSeek 账单计费（$0.75 / 1,000 次）。";
   } else {
    hint.Text="每次成功搜索或读取按 DeepSeek 账单计费（$0.75 / 1,000 次）。网页内容可能包含诱导性文字，曳尔沐只把它当资料，不执行网页里的指令。";
   }
   bool enabled=!busy&&WebApi.Available(pet.State,out reason);
   if(search!=null)search.IsEnabled=enabled;
   if(fetch!=null)fetch.IsEnabled=enabled;
  }
  void ShowPlaceholder() {
   if(results==null)return;
   results.Children.Clear();
   var text=Ui.Text("还没有联网结果。搜索关键词，或粘贴一个网页地址试试。",11,"#795F69");
   text.Margin=new Thickness(0,4,0,2);
   results.Children.Add(text);
  }
  void StartSearch() {
   if(busy)return;
   string text=(query.Text??"").Trim();
   if(text.Length==0) {status.Text="先写一个想搜索的关键词。";return;}
   string reason;
   if(!WebApi.Available(pet.State,out reason)) {status.Text=reason;RefreshStatus();return;}
   string key=ApiKey.Resolve(pet.State).Key;
   string baseUrl=pet.State.ApiBase;
   bool content=needContent.IsChecked==true;
   pet.State.WebNeedContent=content;pet.Save();
   busy=true;RefreshStatus();
   status.Text="◌ 正在搜索「"+WebTask.Clip(text,36)+"」…";
   resultTitle.Text="搜索结果";
   results.Children.Clear();
   results.Children.Add(Ui.Text("正在请求 DeepSeek Search，请稍等…",11,"#795F69"));
   work.Run(delegate {
    return WebApi.Search(key,baseUrl,text,8,content,30);
   },delegate(WebOutcome outcome) {OnSearch(text,outcome);});
  }
  void StartFetch(string address) {
   if(busy)return;
   string link=(address??"").Trim();
   if(!WebApi.SafeUrl(link)) {status.Text="请填写 http 或 https 开头的完整网页地址。";return;}
   string reason;
   if(!WebApi.Available(pet.State,out reason)) {status.Text=reason;RefreshStatus();return;}
   string key=ApiKey.Resolve(pet.State).Key;
   string baseUrl=pet.State.ApiBase;
   busy=true;RefreshStatus();
   status.Text="◌ 正在读取网页…";
   resultTitle.Text="网页正文";
   results.Children.Clear();
   results.Children.Add(Ui.Text("正在请求 DeepSeek Fetch，请稍等…",11,"#795F69"));
   work.Run(delegate {
    return WebApi.Fetch(key,baseUrl,link,30);
   },delegate(WebOutcome outcome) {OnFetch(outcome);});
  }
  void OnSearch(string text,WebOutcome outcome) {
   if(closed)return;
   busy=false;RefreshStatus();
   if(outcome==null||!outcome.Ok) {
    status.Text="○ 搜索没有完成"+(outcome==null?"":"："+outcome.Error);
    results.Children.Clear();
    AddMessage("搜索没有完成",outcome==null?"没有收到结果。":outcome.Error,"#A63C51");
    return;
   }
   status.Text="● 找到 "+outcome.SourceCount+" 条结果 · "+outcome.Seconds.ToString("0.0")+"s";
   resultTitle.Text="搜索结果 · "+WebTask.Clip(text,36);
   results.Children.Clear();
   if(outcome.Results==null||outcome.Results.Length==0) {
    AddMessage("没有找到相关网页","换一个更具体的关键词，或者直接读取一个已知网址。","#78616A");
    return;
   }
   for(int i=0;i<outcome.Results.Length;i++) results.Children.Add(SourceCard(outcome.Results[i],i));
  }
  void OnFetch(WebOutcome outcome) {
   if(closed)return;
   busy=false;RefreshStatus();
   if(outcome==null||!outcome.Ok) {
    status.Text="○ 读取没有完成"+(outcome==null?"":"："+outcome.Error);
    results.Children.Clear();
    AddMessage("读取没有完成",outcome==null?"没有收到网页内容。":outcome.Error,"#A63C51");
    return;
   }
   status.Text="● 已读取 "+outcome.Content.Length+" 字 · "+outcome.Seconds.ToString("0.0")+"s";
   results.Children.Clear();
   var card=new Border{Background=Ui.Brush("#FFFCF9"),BorderBrush=Ui.Brush("#E9D6CF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(14),Margin=new Thickness(0,0,0,12)};
   var panel=new StackPanel();
   var title=Ui.Text(outcome.Title,14.5,"#4F3B43");title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,5);
   panel.Children.Add(title);
   panel.Children.Add(Ui.Text(outcome.Url,10.5,"#795F69"));
   panel.Children.Add(Ui.Row(LinkButton("打开网页",outcome.Url),CopyButton(outcome.Url)));
   var body=Ui.Input(outcome.Content,260);
   body.IsReadOnly=true;body.AcceptsReturn=true;body.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;body.VerticalContentAlignment=VerticalAlignment.Top;body.FontSize=12.5;
   body.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   panel.Children.Add(body);
   card.Child=panel;
   results.Children.Add(card);
  }
  UIElement SourceCard(WebSource source,int index) {
   var card=new Border{Background=Ui.Brush("#FFFCF9"),BorderBrush=Ui.Brush("#E9D6CF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(14),Margin=new Thickness(0,0,0,12)};
   var panel=new StackPanel();
   var title=Ui.Text(source.title,14.5,"#4F3B43");
   title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,4);
   panel.Children.Add(title);
   string meta=(index+1)+" · "+(source.site_name.Length>0?source.site_name:"网页");
   string date=WebApi.DisplayDate(source.date);
   if(date.Length>0)meta+=" · "+date;
   if(source.authority.Length>0)meta+=" · "+source.authority;
   panel.Children.Add(Ui.Text(meta,10.5,"#795F69"));
   string summary=source.BestText;
   if(summary.Length>0) {
    var text=Ui.Text(WebTask.Clip(summary,520),12,"#78616A");text.LineHeight=19;text.Margin=new Thickness(0,6,0,4);
    panel.Children.Add(text);
   }
   var link=Ui.Text(source.url,10.5,"#447969");link.Margin=new Thickness(0,0,0,8);
   panel.Children.Add(link);
   string address=source.url;
   panel.Children.Add(Ui.Row(LinkButton("打开网页",address),CopyButton(address),Ui.Button("读取正文",delegate {url.Text=address;StartFetch(address);},false)));
   card.Child=panel;
   return card;
  }
  void AddMessage(string title,string message,string color) {
   var card=new Border{Background=Ui.Brush("#FFFCF9"),BorderBrush=Ui.Brush("#E9D6CF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(14),Margin=new Thickness(0,0,0,12)};
   var panel=new StackPanel();
   var head=Ui.Text(title,14,"#4F3B43");head.FontWeight=FontWeights.SemiBold;head.Margin=new Thickness(0,0,0,5);
   panel.Children.Add(head);
   panel.Children.Add(Ui.Text(message,11.5,color));
   card.Child=panel;
   results.Children.Add(card);
  }
  Button LinkButton(string label,string address) {
   return Ui.Button(label,delegate {OpenUrl(address);},false);
  }
  Button CopyButton(string address) {
   return Ui.Button("复制链接",delegate {
    try {Clipboard.SetText(address ?? "");status.Text="链接已复制到剪贴板。";}
    catch(Exception e) {status.Text="复制失败："+e.Message;}
   },false);
  }
  static void OpenUrl(string address) {
   if(!WebApi.SafeUrl(address))return;
   try {Process.Start(address);} catch {}
  }
  public void Refresh() {RefreshStatus();}
  public void Dispose() {closed=true;work.Dispose();}
 }
}
