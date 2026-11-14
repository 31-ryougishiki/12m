using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ElymsPet {
 /// <summary>共享的“去 B 站逛逛”任务：自己选题、搜索、读简介和评论，再写一段逛后感。</summary>
 public sealed class BiliService : IDisposable {
  readonly PetState settings;
  readonly Func<string> profileDigest;
  readonly WorkScope work;
  bool fromChat;
  public BiliBrowseResult Current;
  public bool Busy;
  public event Action Changed;
  public event Action<BiliBrowseResult> Finished;
  public BiliService(PetState settings,IWorkScheduler scheduler,Func<string> profileDigest,bool preview) {
   this.settings=settings; this.profileDigest=profileDigest;
   work=new WorkScope(scheduler,OnFailure,!preview);
  }
  void OnFailure(string error) {
   bool browsing=Busy;
   Busy=false; Current=new BiliBrowseResult {Error=error,FromChat=fromChat};
   Raise(); if(browsing&&Finished!=null)Finished(Current);
  }
  public void Dispose() { work.Dispose(); Busy=false; }
  public string StatusLine {
   get {
    if(Busy) return "◌ 正在 B 站逛…";
    if(Current==null) return "● 公开内容 · 只读 · 不需要登录";
    if(!Current.Ok) return "○ 上次没有逛成："+Current.Error;
    return "● 上次逛到《"+BiliService.Clip(Current.Selected==null?"":Current.Selected.Title,28)+"》 · "+Current.Seconds.ToString("0.0")+"s";
   }
  }
  public bool Browse(string query,bool fromChat) {
   if(!PluginAccess.Enabled(settings,"bili")){Current=new BiliBrowseResult{Error="请先安装并启用 B站插件。"};Raise();return false;}
   if(work.IsDisposed||Busy) return false;
   this.fromChat=fromChat;
   Busy=true;Current=null;Raise();
   string key=ApiKey.Resolve(settings).Key;
   string baseUrl=settings.ApiBase,model=settings.ApiModel;
   string digest=profileDigest();
   string dsh=DshApi.PromptContext(settings);
   string web=WebApi.PromptContext(settings);
   PetState state=settings.Snapshot();
   bool ai=settings.AiChat&&ApiKey.Valid(key);
   work.Run(delegate {
    return BiliExplorer.Explore(query,key,ai,baseUrl,model,state,digest,dsh,web);
   },delegate(BiliBrowseResult result) {
    Busy=false;Current=result;if(result!=null)result.FromChat=fromChat;
    Raise();
    if(Finished!=null)Finished(result);
   });
   return true;
  }
  public void DraftComment(BiliVideo video,Action<string> done) {
   if(!PluginAccess.Enabled(settings,"bili")){if(done!=null)done("请先安装 B站插件。");return;}
   string key=ApiKey.Resolve(settings).Key;
   string baseUrl=settings.ApiBase,model=settings.ApiModel;
   string digest=profileDigest();
   string dsh=DshApi.PromptContext(settings);
   string web=WebApi.PromptContext(settings);
   PetState state=settings.Snapshot();
   bool ai=settings.AiChat&&ApiKey.Valid(key);
   work.Run(delegate {
    return BiliExplorer.DraftComment(video,key,ai,baseUrl,model,state,digest,dsh,web);
   },delegate(string text) {if(done!=null)done(text);},delegate(string error) {if(done!=null)done("");});
  }
  public void SendComment(BiliVideo video,string text,Action<BiliCommentResult> done) {
   if(!PluginAccess.Enabled(settings,"bili")){if(done!=null)done(new BiliCommentResult{Error="请先安装 B站插件。"});return;}
   string csrf=BiliApi.CsrfToken();
   work.Run(delegate {
    try {return BiliApi.PostComment(video,text,csrf,25);}
    catch(BiliException e) {return new BiliCommentResult {Error=e.Message};}
    catch(Exception e) {return new BiliCommentResult {Error="发送评论时出了点问题："+e.Message};}
   },delegate(BiliCommentResult result) {if(done!=null)done(result);},delegate(string error) {if(done!=null)done(new BiliCommentResult {Error=error});});
  }
  public void DeleteComment(BiliVideo video,long rpid,Action<BiliCommentResult> done) {
   if(!PluginAccess.Enabled(settings,"bili")){if(done!=null)done(new BiliCommentResult{Error="请先安装 B站插件。"});return;}
   string csrf=BiliApi.CsrfToken();
   work.Run(delegate {
    try {return BiliApi.DeleteComment(video,rpid,csrf,25);}
    catch(BiliException e) {return new BiliCommentResult {Error=e.Message};}
    catch(Exception e) {return new BiliCommentResult {Error="删除评论时出了点问题："+e.Message};}
   },delegate(BiliCommentResult result) {if(done!=null)done(result);},delegate(string error) {if(done!=null)done(new BiliCommentResult {Error=error});});
  }
  void Raise() {if(Changed!=null)Changed();}
  public static string Clip(string text,int max) {
   text=(text??"").Trim();
   return text.Length<=max?text:text.Substring(0,max)+"…";
  }
 }
}
