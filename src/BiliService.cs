using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ElymsPet {
 /// <summary>共享的“去 B 站逛逛”任务：自己选题、搜索、读简介和评论，再写一段逛后感。</summary>
 public sealed class BiliService {
  readonly PetWindow pet;
  public BiliBrowseResult Current;
  public bool Busy;
  public event Action Changed;
  public event Action<BiliBrowseResult> Finished;
  public BiliService(PetWindow target) {pet=target;}
  public string StatusLine {
   get {
    if(Busy) return "◌ 正在 B 站逛…";
    if(Current==null) return "● 公开内容 · 只读 · 不需要登录";
    if(!Current.Ok) return "○ 上次没有逛成："+Current.Error;
    return "● 上次逛到《"+BiliService.Clip(Current.Selected==null?"":Current.Selected.Title,28)+"》 · "+Current.Seconds.ToString("0.0")+"s";
   }
  }
  public bool Browse(string query,bool fromChat) {
   if(Busy) return false;
   Busy=true;Current=null;Raise();
   string key=ApiKey.Resolve(pet.State).Key;
   string baseUrl=pet.State.ApiBase,model=pet.State.ApiModel;
   string digest=pet.ProfileDigest;
   string dsh=pet.Dsh.PromptContext();
   string web=WebApi.PromptContext(pet.State);
   PetState state=pet.State;
   Ui.InBackground(pet.Dispatcher,delegate {
    return BiliExplorer.Explore(query,key,ApiKey.Valid(key),baseUrl,model,state,digest,dsh,web);
   },delegate(BiliBrowseResult result) {
    Busy=false;Current=result;if(result!=null)result.FromChat=fromChat;
    Raise();
    if(Finished!=null)Finished(result);
   });
   return true;
  }
  public void DraftComment(BiliVideo video,Action<string> done) {
   string key=ApiKey.Resolve(pet.State).Key;
   string baseUrl=pet.State.ApiBase,model=pet.State.ApiModel;
   string digest=pet.ProfileDigest;
   string dsh=pet.Dsh.PromptContext();
   string web=WebApi.PromptContext(pet.State);
   PetState state=pet.State;
   Ui.InBackground(pet.Dispatcher,delegate {
    return BiliExplorer.DraftComment(video,key,ApiKey.Valid(key),baseUrl,model,state,digest,dsh,web);
   },delegate(string text) {if(done!=null)done(text);});
  }
  public void SendComment(BiliVideo video,string text,Action<BiliCommentResult> done) {
   string csrf=BiliApi.CsrfToken();
   Ui.InBackground(pet.Dispatcher,delegate {
    try {return BiliApi.PostComment(video,text,csrf,25);}
    catch(BiliException e) {return new BiliCommentResult {Error=e.Message};}
    catch(Exception e) {return new BiliCommentResult {Error="发送评论时出了点问题："+e.Message};}
   },delegate(BiliCommentResult result) {if(done!=null)done(result);});
  }
  public void DeleteComment(BiliVideo video,long rpid,Action<BiliCommentResult> done) {
   string csrf=BiliApi.CsrfToken();
   Ui.InBackground(pet.Dispatcher,delegate {
    try {return BiliApi.DeleteComment(video,rpid,csrf,25);}
    catch(BiliException e) {return new BiliCommentResult {Error=e.Message};}
    catch(Exception e) {return new BiliCommentResult {Error="删除评论时出了点问题："+e.Message};}
   },delegate(BiliCommentResult result) {if(done!=null)done(result);});
  }
  void Raise() {if(Changed!=null)Changed();}
  public static string Clip(string text,int max) {
   text=(text??"").Trim();
   return text.Length<=max?text:text.Substring(0,max)+"…";
  }
 }
 /// <summary>把 B 站公开数据整理成一次“逛”的结果；不登录、不点赞、不评论。</summary>
 public static class BiliExplorer {
  static readonly string[] FallbackTopics={"冷门纪录片","独立游戏 开发","天文 科普","机械键盘","城市散步","手作木工","AI 绘画","电影拉片","植物图鉴","旧电脑修复"};
  public static BiliBrowseResult Explore(string query,string key,bool hasKey,string baseUrl,string model,PetState state,string digest,string dshContext,string webContext) {
   var watch=Stopwatch.StartNew();
   var result=new BiliBrowseResult();
   try {
    string topic=(query??"").Trim();
    if(topic.Length==0) topic=ChooseTopic(key,hasKey,baseUrl,model,digest);
    if(topic.Length==0) topic="B站热门";
    result.Topic=BiliService.Clip(topic,40);
    BiliVideo[] videos=null;
    try {videos=BiliApi.Search(topic,8,25);}
    catch(BiliException e) {result.Note="搜索没有成功："+e.Message;}
    if(videos==null||videos.Length==0) {
     try {
      videos=BiliApi.Ranking(8,25);
      result.Note=(result.Note.Length>0?result.Note+" ":"")+"改看了 B 站热门。";
     } catch(BiliException e) {
      result.Error=(result.Note.Length>0?result.Note+" ":"")+e.Message;
      result.Seconds=watch.Elapsed.TotalSeconds;
      return result;
     }
    }
    if(videos==null||videos.Length==0) {result.Error="这次没有找到可以逛的视频。";result.Seconds=watch.Elapsed.TotalSeconds;return result;}
    result.Videos=videos;
    int pick=videos.Length>2?new Random().Next(Math.Min(3,videos.Length)):0;
    BiliVideo selected=videos[pick];
    try {selected=BiliApi.LoadVideo(selected.Bvid,25);}
    catch(BiliException e) {result.Note=(result.Note.Length>0?result.Note+" ":"")+"详情没有取全："+e.Message;}
    result.Selected=selected;
    try {result.Comments=BiliApi.Comments(selected,5,25);}
    catch(BiliException e) {result.Comments=new BiliComment[0];result.Note=(result.Note.Length>0?result.Note+" ":"")+"评论没有取到："+e.Message;}
    result.Summary=Summarize(topic,selected,result.Comments,videos,key,hasKey,baseUrl,model,state,digest,dshContext,webContext);
    if(result.Summary.Length==0) result.Summary=LocalSummary(topic,selected,result.Comments);
    result.Ok=true;
    result.Seconds=watch.Elapsed.TotalSeconds;
    return result;
   } catch(Exception e) {
    result.Error="逛 B 站时出了点问题："+e.Message;
    result.Seconds=watch.Elapsed.TotalSeconds;
    return result;
   }
  }
  static string ChooseTopic(string key,bool hasKey,string baseUrl,string model,string digest) {
   var random=new Random();
   if(!hasKey) return FallbackTopics[random.Next(FallbackTopics.Length)];
   var turns=new ChatTurn[] {
    new ChatTurn("system","你是曳尔沐，正在准备去 B 站随便逛逛。根据对方的画像，选一个你今晚真的想看的搜索词。只输出 2—10 个字的关键词，不要引号、不要解释。"),
    new ChatTurn("user","对方的画像摘要："+BiliService.Clip(digest,500)+"\n请只输出一个 B 站搜索词。")
   };
   ChatOutcome outcome=DeepSeek.Complete(key,baseUrl,model,false,turns,25,80,0.9);
   string topic=CleanTopic(outcome.Ok?outcome.Text:"");
   return topic.Length>0?topic:FallbackTopics[random.Next(FallbackTopics.Length)];
  }
  static string CleanTopic(string text) {
   text=(text??"").Trim();
   text=text.Replace("\r"," ").Replace("\n"," ");
   text=text.Trim('"','“','”','\'','‘','’','《','》','【','】',' ');
   if(text.Length>20) text=text.Substring(0,20);
   return text;
  }
  static string Summarize(string topic,BiliVideo video,BiliComment[] comments,BiliVideo[] videos,string key,bool hasKey,string baseUrl,string model,PetState state,string digest,string dshContext,string webContext) {
   if(!hasKey||video==null) return "";
   var user=new StringBuilder();
   user.Append("刚才我在 B 站逛了「").Append(topic).Append("」，刷到这些视频：\n");
   for(int i=0;i<videos.Length&&i<5;i++) user.Append("- ").Append(BiliService.Clip(videos[i].Title,80)).Append("（UP主 ").Append(BiliService.Clip(videos[i].Author,30)).Append("，播放 ").Append(videos[i].PlayText).Append("）\n");
   user.Append("\n我最后点开了：\n");
   user.Append("标题：").Append(video.Title).Append("\n");
   user.Append("UP主：").Append(video.Author).Append(" · 播放 ").Append(video.PlayText).Append(" · 弹幕 ").Append(BiliApi.FormatCount(video.Danmaku)).Append(" · 时长 ").Append(video.DurationText).Append(" · 发布 ").Append(video.DateText).Append("\n");
   user.Append("简介：").Append(BiliService.Clip(video.Description,700)).Append("\n");
   user.Append("链接：").Append(video.Url).Append("\n");
   if(comments.Length>0) {
    user.Append("\n热门评论：\n");
    for(int i=0;i<comments.Length&&i<4;i++) user.Append("- ").Append(BiliService.Clip(comments[i].Author,20)).Append("（").Append(BiliApi.FormatCount(comments[i].Like)).Append("赞）：").Append(BiliService.Clip(comments[i].Message,180)).Append("\n");
   }
   user.Append("\n请用曳尔沐的第一人称写一段 80—160 字的逛后感：为什么停在它上面、简介/评论里哪个点有意思、你自己的一点感受。");
   user.Append("你只读了公开标题、简介和评论，没有看视频画面，不要假装看过画面或听过声音。");
   user.Append("不要用 Markdown 标题或列表；最后另起一行写「来源：标题 - URL」。不要输出任何 [[...]] 标记。");
   var turns=new ChatTurn[] {
    new ChatTurn("system",Persona.SystemPrompt(state,DateTime.Now,digest,dshContext,webContext)),
    new ChatTurn("user",user.ToString())
   };
   ChatOutcome outcome=DeepSeek.Complete(key,baseUrl,model,true,turns,45,1000,0.7);
   if(!outcome.Ok||string.IsNullOrEmpty(outcome.Text)) return "";
   string text=outcome.Text.Replace("[[WEB_SEARCH]]","").Replace("[[/WEB_SEARCH]]","").Replace("[[WEB_FETCH]]","").Replace("[[/WEB_FETCH]]","").Replace("[[DSH_TASK]]","").Replace("[[/DSH_TASK]]","").Trim();
   return BiliService.Clip(text,1400);
  }
  public static string DraftComment(BiliVideo video,string key,bool hasKey,string baseUrl,string model,PetState state,string digest,string dshContext,string webContext) {
   if(video==null) return "";
   if(!hasKey) return "";
   var user=new StringBuilder();
   user.Append("我刚看完这个 B 站视频的公开页面：\n");
   user.Append("标题：").Append(video.Title).Append("\nUP主：").Append(video.Author).Append("\n");
   user.Append("简介：").Append(BiliService.Clip(video.Description,500)).Append("\n");
   user.Append("播放：").Append(video.PlayText).Append(" · 评论：").Append(BiliApi.FormatCount(video.Reply)).Append("\n");
   user.Append("\n请以曳尔沐的口吻写一条 20—70 字的 B 站评论草稿：真诚、具体，不引战、不刷屏、不玩烂梗、不谈政治，也不要假装看过视频画面；只输出评论正文，不要引号、不要解释、不要链接。");
   var turns=new ChatTurn[] {
    new ChatTurn("system",Persona.SystemPrompt(state,DateTime.Now,digest,dshContext,webContext)),
    new ChatTurn("user",user.ToString())
   };
   ChatOutcome outcome=DeepSeek.Complete(key,baseUrl,model,true,turns,30,300,0.8);
   if(!outcome.Ok) return "";
   string text=(outcome.Text??"").Replace("\r"," ").Replace("\n"," ").Trim();
   text=text.Trim('"','“','”','\'','‘','’',' ');
   string[] prefixes={"评论：","评论:","草稿：","草稿:"};
   for(int i=0;i<prefixes.Length;i++) if(text.StartsWith(prefixes[i],StringComparison.Ordinal)) text=text.Substring(prefixes[i].Length).Trim();
   if(text.Length>100) text=text.Substring(0,100);
   return text;
  }
  static string LocalSummary(string topic,BiliVideo video,BiliComment[] comments) {
   if(video==null) return "这次没有逛到合适的视频。";
   var text=new StringBuilder();
   text.Append("我在 B 站逛了「").Append(topic).Append("」，最后停在《").Append(BiliService.Clip(video.Title,60)).Append("》。UP主是 ").Append(BiliService.Clip(video.Author,30)).Append("，播放 ").Append(video.PlayText).Append("。");
   if(video.Description.Length>0) text.Append("简介里说：").Append(BiliService.Clip(video.Description,110));
   if(comments.Length>0) text.Append(" 评论区里 ").Append(BiliService.Clip(comments[0].Author,20)).Append(" 说：").Append(BiliService.Clip(comments[0].Message,90));
   text.Append(" 来源：").Append(video.Url);
   return BiliService.Clip(text.ToString(),1400);
  }
 }
}
