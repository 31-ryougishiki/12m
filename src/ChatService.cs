using System;
using System.Collections.Generic;
using System.Text;

namespace ElymsPet {
 public enum ChatSpeaker { User, Elyms, Notice }

 public sealed class ChatLine {
  public readonly ChatSpeaker Speaker;
  public readonly string Text;
  public readonly DateTime Time;
  public readonly string Meta;
  public ChatLine(ChatSpeaker speaker,string text,string meta) {
   Speaker=speaker;Text=text??"";Time=DateTime.Now;Meta=meta??"";
  }
  public ChatLine(ChatSpeaker speaker,string text,string meta,DateTime time) {
   Speaker=speaker;Text=text??"";Time=time;Meta=meta??"";
  }
 }

 public static class ChatText {
  public static string Clip(string text,int max) {
   text=(text??"").Trim();
   if(text.Length<=max) return text;
   return text.Substring(0,max)+"…";
  }
  public static int SecondsFor(string text) {
   return Math.Max(8,Math.Min(30,(text??"").Length/4));
  }
 }

 /// <summary>一段共享的对话：陪伴面板和浮动输入框都读写这里，保证两窗口看到同一段聊天。</summary>
 public sealed class ChatService : IDisposable {
  readonly PetState state;
  readonly WorkScope work;
  readonly DshWorkbench dsh;
  readonly BiliService bili;
  readonly MemoryService memory;
  readonly ChatSession session;
  readonly Func<string> profileDigest;
  readonly Func<string,string> retrieveProfile;
  readonly Func<string, bool> confirmDsh;
  readonly PetFeedback feedback;
  readonly IClock clock;
  readonly List<ChatLine> lines=new List<ChatLine>();
  bool busy;
  string pendingBiliUser="";
  public string LastHint="在这里哦。想聊些什么？";
  public string LastMeta="待命";
  sealed class WebRun {
   public string User="";
   public string Memory="";
   public string Initial="";
   public string Intent="";
   public bool Fetch;
   public string Evidence="";
   public List<WebSource> Seen=new List<WebSource>();
   public int Sources;
   public int Steps;
   public double Seconds;
   public int CompletionTokens;
   public bool Announced;
  }
  public event Action<ChatLine> LineAdded;
  public event Action Changed;
  public event Action Cleared;
  public ChatService(PetState state, IWorkScheduler scheduler, DshWorkbench dsh, BiliService bili,
   MemoryService memory, ChatSession session, Func<string> profileDigest, PetFeedback feedback,
   Func<string, bool> confirmDsh, IClock clock, bool preview, Func<string,string> retrieveProfile=null) {
   this.state=state; this.dsh=dsh; this.bili=bili; this.memory=memory; this.session=session;
   this.retrieveProfile=retrieveProfile;this.profileDigest=profileDigest; this.feedback=feedback; this.confirmDsh=confirmDsh; this.clock=clock;
   work=new WorkScope(scheduler,OnFailure,!preview);
   session.Load();
   ChatTurn[] history=session.History;
   for(int i=0;i<history.Length;i++) {
    lines.Add(new ChatLine(history[i].Role=="assistant"?ChatSpeaker.Elyms:ChatSpeaker.User,history[i].Content,"历史上下文"));
   }
   while(lines.Count>200)lines.RemoveAt(0);
   dsh.Finished+=OnDshFinished;
   bili.Finished+=OnBiliFinished;
  }
  public bool Busy {get {return busy;}}
  public int ContextTokens {get {return session.ContextTokens;}}
  public int ContextLimit {get {return session.ContextLimit;}}
  public string ContextLabel {
   get {return "上下文 "+ContextTokens.ToString("N0")+" / 1,000,000 tokens · "+(session.LastError.Length==0?"已持久化":"保存失败，请检查数据文件夹");}
  }
  public ChatTurn[] RecentTurns(int count) {return session.Recent(count);}
  public string RecentContext(int count) {
   ChatTurn[] list=session.Recent(count);
   var text=new StringBuilder();
   for(int i=0;i<list.Length;i++) {
    text.Append(list[i].Role=="assistant"?"曳尔沐：":"对方：");
    text.Append(ChatText.Clip(list[i].Content,300)).Append("\n");
   }
   return text.ToString();
  }
  public ChatLine[] Lines {get {return lines.ToArray();}}
  public void Send(string raw) {
   if(work.IsDisposed)return;
   if(busy) {Add(new ChatLine(ChatSpeaker.Notice,"正在等曳尔沐回复，这句话稍后再发吧。",""));return;}
   string text=(raw??"").Trim();
   if(text.Length==0) return;
   if(text.Length>4000) text=text.Substring(0,4000);
   feedback.Wake();
   Add(new ChatLine(ChatSpeaker.User,text,""));
   ApiKey.State key=ApiKey.Resolve(state);
   if(!state.AiChat) {Offline(text,"在线 AI 已关闭，这次先用离线对白陪你。");return;}
   if(!key.Configured) {Offline(text,"还没有配置 API Key。在「设置」里接入 DeepSeek，就能在线聊天。");return;}
   string memoryContext=memory.Recall(text);
   string biliQuery;
   if(TryBiliCommand(text,out biliQuery)) {
    busy=true;pendingBiliUser=text;LastHint="正在B站逛逛…";LastMeta=LastHint;
    Add(new ChatLine(ChatSpeaker.Notice,biliQuery.Length>0?"我去B站逛逛「"+WebTask.Clip(biliQuery,36)+"」…":"我去B站随便逛逛，看看能刷到什么…",""));
    if(!bili.Browse(biliQuery,true)) {
     busy=false;pendingBiliUser="";
     Add(new ChatLine(ChatSpeaker.Notice,"B站这一趟还没结束，等它逛完再试。",""));
    }
    Raise();
    return;
   }
   string command;bool commandFetch;
   if(TryWebCommand(text,out command,out commandFetch)) {
    if(!state.WebEnabled) {Offline(text,"联网搜索在设置里关闭了；到「设置 → AI 对话」打开后，我就能帮你查。");return;}
    busy=true;LastHint=commandFetch?"正在读取网页…":"正在联网搜索…";LastMeta=LastHint;Raise();
    var run=new WebRun {User=text,Memory=memoryContext,Intent=command,Fetch=commandFetch};
    RunWebStep(run);
    return;
   }
   bool autoFetch;string autoValue;
   if(state.WebEnabled&&WebApi.NeedsFreshInfo(text,out autoFetch,out autoValue)) {
    busy=true;LastHint=autoFetch?"正在读取网页…":"正在联网搜索…";LastMeta=LastHint;Raise();
    Add(new ChatLine(ChatSpeaker.Notice,autoFetch?"这句话提到一个网页，我先读取正文再回答。":"这句话可能需要最新信息，我先联网查一下。",""));
    var autoRun=new WebRun {User=text,Memory=memoryContext,Intent=autoValue,Fetch=autoFetch,Announced=true};
    RunWebStep(autoRun);
    return;
   }
   ChatTurn[] turns=BuildTurns(text,memoryContext,"");
   string baseUrl=state.ApiBase,model=state.ApiModel;
   // 聊天固定开启思考；不再依赖旧设置里的“深度思考”开关。
   busy=true;LastHint="正在接通 DeepSeek，先想一想…";LastMeta=LastHint;Raise();
   feedback.Act(6,"正在想一想，稍等一下…",20);
   int timeout=session.ContextTokens>100000?120:45;
   work.Run(delegate {
    return DeepSeek.Complete(key.Key,baseUrl,model,true,turns,timeout);
   },delegate(ChatOutcome outcome) {Finish(text,outcome,memoryContext);});
  }
  static bool TryWebCommand(string text,out string value,out bool fetch) {
   value="";fetch=false;
   string t=(text??"").Trim();
   string[] search={"/搜索 ","/搜 ","/search "};
   string[] read={"/读取 ","/读 ","/fetch "};
   for(int i=0;i<search.Length;i++) if(t.StartsWith(search[i],StringComparison.OrdinalIgnoreCase)) {value=t.Substring(search[i].Length).Trim();return value.Length>0;}
   for(int i=0;i<read.Length;i++) if(t.StartsWith(read[i],StringComparison.OrdinalIgnoreCase)) {value=t.Substring(read[i].Length).Trim();fetch=true;return value.Length>0;}
   return false;
  }
  static bool TryBiliCommand(string text,out string query) {
   query="";
   string t=(text??"").Trim();
   string[] prefixes={"/逛b站","/b站","/bili"};
   for(int i=0;i<prefixes.Length;i++) {
    if(t.StartsWith(prefixes[i],StringComparison.OrdinalIgnoreCase)) {
     query=t.Substring(prefixes[i].Length).Trim();
     return true;
    }
   }
   string lower=t.ToLowerInvariant();
   string[] cues={"去b站逛逛","逛逛b站","看看b站","去b站看看","b站有什么好看","b站有什么有意思"};
   for(int i=0;i<cues.Length;i++) if(lower.IndexOf(cues[i],StringComparison.Ordinal)>=0) {query="";return true;}
   return false;
  }
  void OnBiliFinished(BiliBrowseResult result) {
   if(result==null||!result.FromChat)return;
   busy=false;
   if(!result.Ok) {
    Add(new ChatLine(ChatSpeaker.Notice,"B站没有逛成："+ChatText.Clip(result.Error,180),""));
    string fallback="这次没能在B站逛下去。等网络或B站风控缓一缓，再让我去试试。";
    session.Remember(pendingBiliUser,fallback);
    Add(new ChatLine(ChatSpeaker.Elyms,fallback,"B站 · 未完成"));
    LastHint="B站没有逛成";LastMeta=LastHint;
    pendingBiliUser="";
    Raise();
    return;
   }
   string meta="B站 · "+result.Seconds.ToString("0.0")+"s · 评论 "+result.Comments.Length+" 条";
   Add(new ChatLine(ChatSpeaker.Notice,"B站逛完了 · 刷到 "+result.Videos.Length+" 个视频 · "+result.Seconds.ToString("0.0")+"s",""));
   Add(new ChatLine(ChatSpeaker.Elyms,result.Summary,meta));
   if(result.Selected!=null) Add(new ChatLine(ChatSpeaker.Notice,"她停在这里：\n"+result.Selected.Title+"\n"+result.Selected.Url,""));
   session.Remember(pendingBiliUser,result.Summary);
   LastHint=meta;LastMeta=meta;
   feedback.Act(6,ChatText.Clip(result.Summary,120),ChatText.SecondsFor(result.Summary));
   pendingBiliUser="";
   Raise();
  }
  public void Clear() {
   if(busy) {Add(new ChatLine(ChatSpeaker.Notice,"等这次回复回来，再清空对话吧。",""));return;}
   session.Clear();lines.Clear();
   LastHint="对话记忆已经清空。";LastMeta="已清空";
   if(Cleared!=null)Cleared();
   Raise();
  }
  public void SeedPreview(string user,string reply) {
   Add(new ChatLine(ChatSpeaker.User,user,""));
   Add(new ChatLine(ChatSpeaker.Elyms,reply,"DeepSeek · 1.1s · 27 tokens"));
   LastHint="DeepSeek V4.1 Flash · 1.1s · 27 tokens";LastMeta=LastHint;
  }
  /// <summary>自主时间里由她主动发起的一段话，进入共享对话和长期上下文。</summary>
  public void AddAutonomous(string text,string meta) {
   text=(text??"").Trim();
   if(text.Length==0)return;
   Add(new ChatLine(ChatSpeaker.Elyms,text,meta??"自主活动"));
   session.RememberAssistant(text);
   LastHint=meta??"自主活动";LastMeta=LastHint;
   feedback.Act(6,ChatText.Clip(text,120),ChatText.SecondsFor(text));
   Raise();
  }
  public void AddAutonomyNotice(string text) {
   if(!string.IsNullOrEmpty(text)) Add(new ChatLine(ChatSpeaker.Notice,text,""));
  }
  void Finish(string user,ChatOutcome outcome,string memoryContext) {
   if(outcome==null||!outcome.Ok) {Offline(user,outcome==null?"这次没有收到回复。":outcome.Error);return;}
   string reply=outcome.Text;
   string dshTask=DshTask.Extract(ref reply);
   WebIntent web=WebTask.Extract(ref reply);
   MemoryItem[] memories=MemoryMarkers.Extract(ref reply);
   int remembered=(memory==null||memories.Length==0)?0:memory.Remember(memories);
   if(web.Present&&dshTask.Length==0) {
    if(state.WebEnabled) {
     if(reply.Length>0) Add(new ChatLine(ChatSpeaker.Elyms,reply,"DeepSeek · 准备联网…"));
     var run=new WebRun {User=user,Memory=memoryContext,Initial=reply,Intent=web.Value,Fetch=web.Fetch};
     run.Seconds=outcome.Seconds;run.CompletionTokens=outcome.CompletionTokens;
     RunWebStep(run);
     return;
    }
    Add(new ChatLine(ChatSpeaker.Notice,"联网搜索在设置里关闭了，这次先不查网页。",""));
   }
   busy=false;
   if(reply.Length==0)reply=remembered>0?"嗯，这件事我记下了。":(dshTask.Length>0?"好，我把这件事放到 DSH 工作台，等你确认。":(web.Present?"联网搜索关闭了，这次没有查到网页资料。":outcome.Text));
   session.Remember(user,reply);
   string meta="DeepSeek · "+outcome.Seconds.ToString("0.0")+"s · "+outcome.CompletionTokens+" tokens";
   Add(new ChatLine(ChatSpeaker.Elyms,reply,meta));
   if(remembered>0) Add(new ChatLine(ChatSpeaker.Notice,"记住了一条："+MemoryStore.Summarize(memories),""));
   LastHint=meta;LastMeta=meta;
   feedback.Act(6,ChatText.Clip(reply,120),ChatText.SecondsFor(reply));
   if(dshTask.Length>0)HandleDshProposal(dshTask);
   Raise();
  }
  void Offline(string user,string hint) {
   busy=false;
   string local=Dialogue.Reply(user);
   session.Remember(user,local);
   Add(new ChatLine(ChatSpeaker.Notice,hint,""));
   Add(new ChatLine(ChatSpeaker.Elyms,local,"离线对白"));
   LastHint=hint;LastMeta="离线对白";
   feedback.Act(6,ChatText.Clip(local,120),ChatText.SecondsFor(local));
   Raise();
  }
  void RunWebStep(WebRun run) {
   run.Steps++;
   string action=run.Fetch?"正在读取网页…":"正在联网搜索…";
   LastHint=action;LastMeta=action;
   if(!run.Announced) {
    run.Announced=true;
    Add(new ChatLine(ChatSpeaker.Notice,run.Fetch?"正在读取网页资料，稍等一下…":"正在联网搜索「"+WebTask.Clip(run.Intent,40)+"」，稍等一下…",""));
   }
   Raise();
   feedback.Act(6,run.Fetch?"正在打开网页取资料…":"正在联网找资料…",25);
   string key=ApiKey.Resolve(state).Key,baseUrl=state.ApiBase;
   string intent=run.Intent;bool fetch=run.Fetch;bool needContent=state.WebNeedContent;
   work.Run(delegate {
    return fetch?WebApi.Fetch(key,baseUrl,intent,30):WebApi.Search(key,baseUrl,intent,5,needContent,30);
   },delegate(WebOutcome outcome) {OnWebOutcome(run,outcome);});
  }
  void OnWebOutcome(WebRun run,WebOutcome outcome) {
   if(outcome==null||!outcome.Ok) {FailWeb(run,outcome==null?"没有收到联网结果。":outcome.Error);return;}
   if(outcome.SourceCount==0) {FailWeb(run,"没有找到相关网页。");return;}
   run.Seconds+=outcome.Seconds;
   run.Sources+=outcome.SourceCount;
   if(outcome.Kind==WebKind.Fetch) run.Seen.Add(new WebSource{title=outcome.Title,url=outcome.Url});
   else if(outcome.Results!=null) run.Seen.AddRange(outcome.Results);
   run.Evidence+=WebApi.ModelEvidence(run.Intent,outcome);
   Add(new ChatLine(ChatSpeaker.Notice,outcome.Kind==WebKind.Fetch?"已读取网页正文 · 正在整理…":"已取回 "+outcome.SourceCount+" 条公开网页资料 · 正在整理…",""));
   ChatTurn[] turns=BuildTurns(run.User,run.Memory,run.Evidence);
   string key=ApiKey.Resolve(state).Key,baseUrl=state.ApiBase,model=state.ApiModel;
   LastHint="正在整理联网结果…";LastMeta=LastHint;Raise();
   feedback.Act(6,"资料拿到了，我来整理一下…",25);
   work.Run(delegate {
    return DeepSeek.Complete(key,baseUrl,model,true,turns,60,1600,0.4);
   },delegate(ChatOutcome outcome2) {FinishWeb(run,outcome2);});
  }
  void FinishWeb(WebRun run,ChatOutcome outcome) {
   if(outcome==null||!outcome.Ok) {FailWeb(run,outcome==null?"这次没有整理出回答。":outcome.Error);return;}
   run.Seconds+=outcome.Seconds;run.CompletionTokens+=outcome.CompletionTokens;
   string reply=outcome.Text;
   DshTask.Extract(ref reply);
   WebIntent next=WebTask.Extract(ref reply);
   MemoryItem[] memories=MemoryMarkers.Extract(ref reply);
   if(memories.Length>0&&memory!=null)memory.Remember(memories);
   if(next.Present&&run.Steps<2&&state.WebEnabled) {
    run.Intent=next.Value;run.Fetch=next.Fetch;
    Add(new ChatLine(ChatSpeaker.Notice,next.Fetch?"再读取一个网页核对一下…":"再补查一条资料…",""));
    RunWebStep(run);
    return;
   }
   busy=false;
   if(reply.Length==0)reply="资料已经取回，但这次没能整理成回答。可以到「联网」页打开原始网页看看。";
   session.Remember(run.User,reply);
   AddWebSources(run,reply);
   string meta="DeepSeek + 联网 · "+run.Seconds.ToString("0.0")+"s · 检索 "+run.Sources+" 条";
   Add(new ChatLine(ChatSpeaker.Elyms,reply,meta));
   LastHint=meta;LastMeta=meta;
   feedback.Act(6,ChatText.Clip(reply,120),ChatText.SecondsFor(reply));
   Raise();
  }
  void AddWebSources(WebRun run,string reply) {
   if(run.Seen.Count==0)return;
   string text="";int count=0;
   for(int i=0;i<run.Seen.Count&&count<3;i++) {
    WebSource source=run.Seen[i];
    if(string.IsNullOrEmpty(source.url))continue;
    if(reply.IndexOf(source.url,StringComparison.OrdinalIgnoreCase)>=0)continue;
    text+=(text.Length>0?"\n":"")+"· "+ChatText.Clip(source.title,42)+"\n"+source.url;
    count++;
   }
   if(text.Length>0) Add(new ChatLine(ChatSpeaker.Notice,"参考资料：\n"+text,""));
  }
  void FailWeb(WebRun run,string error) {
   busy=false;
   Add(new ChatLine(ChatSpeaker.Notice,"联网没有完成："+ChatText.Clip(error,180),""));
   string fallback=(run.Initial??"").Trim();
   if(fallback.Length==0) {
    fallback="这次没有查到可用的资料。可以换个关键词，或者稍后再试。";
    Add(new ChatLine(ChatSpeaker.Elyms,fallback,"联网未完成"));
   }
   session.Remember(run.User,fallback);
   LastHint="联网没有完成";LastMeta="联网未完成";
   feedback.Act(6,"这次联网没有接通，稍后再试试。",6);
   Raise();
  }
  void HandleDshProposal(string task) {
   task=(task??"").Trim();
   if(task.Length==0)return;
   if(!state.DshEnabled) {Add(new ChatLine(ChatSpeaker.Notice,"DSH 委托在设置里关闭了，这次先不实际动手。",""));return;}
   string error;var handshake=DshApi.ReadHandshake(out error);
   if(handshake==null) {Add(new ChatLine(ChatSpeaker.Notice,"DSH 工作台没有运行，等打开 DSH 后我再把这件事交过去。",""));return;}
   string permission=state.DshFullAccess?"danger-full-access":"workspace-write";
   bool run=state.DshAutoRun;
   if(!run) {
    string warning=permission=="danger-full-access"?"DSH 会获得完整访问权限，可以执行命令、修改工作区。":"DSH 会在工作区里读写文件。";
    string message="曳尔沐想把这件事交给 DSH 工作台：\n\n"+DshTask.Clip(task,600)+"\n\n"+warning+"\n现在执行吗？";
    run=confirmDsh(message);
   }
   if(!run) {Add(new ChatLine(ChatSpeaker.Notice,"好，先不做。想执行时可以在「DSH 工作台」里手动提交。",""));return;}
   string title="曳尔沐 · "+DshTask.Clip(task.Replace("\r"," ").Replace("\n"," "),28);
   if(dsh.Start(task,permission,title)) {
    Add(new ChatLine(ChatSpeaker.Notice,"已交给 DSH 工作台（"+(permission=="danger-full-access"?"完整访问":"工作区可写")+"），完成后我把结果带回来。",""));
    LastHint="DSH 任务已提交，等待结果…";LastMeta=LastHint;Raise();
    feedback.Say("正在把这件事交给 DSH…",8);
   } else {
    Add(new ChatLine(ChatSpeaker.Notice,"没能交给 DSH："+(dsh.Current==null?"DSH 没有运行":dsh.Current.Error),""));
   }
  }
  void OnDshFinished(DshJob job) {
   if(job==null)return;
   if(job.Status=="done") {
    string text=(job.Text??"").Trim();
    if(text.Length==0)text=string.IsNullOrEmpty(job.Note)?"DSH 结束了，但没有返回文字总结。":job.Note;
    string meta="DSH · "+job.Seconds.ToString("0.0")+"s · 工具 "+job.Tools+" 次";
    Add(new ChatLine(ChatSpeaker.Notice,"DSH 任务完成 · "+job.Seconds.ToString("0.0")+"s · 工具 "+job.Tools+" 次",""));
    Add(new ChatLine(ChatSpeaker.Elyms,DshTask.Clip(text,1200),meta));
    session.Remember("（DSH 任务）"+job.Task,"（DSH 结果）"+DshTask.Clip(text,1500));
    feedback.Act(6,DshTask.Clip(text,120),ChatText.SecondsFor(text));
    LastHint=meta;LastMeta=meta;
   } else if(job.Status=="cancelled") {
    Add(new ChatLine(ChatSpeaker.Notice,"DSH 任务已取消。",""));
    LastHint="DSH 任务已取消";LastMeta=LastHint;
   } else {
    Add(new ChatLine(ChatSpeaker.Notice,"DSH 任务没有完成："+(string.IsNullOrEmpty(job.Error)?"未知错误":job.Error),""));
    LastHint="DSH 任务没有完成";LastMeta=LastHint;
   }
   Raise();
  }
  void Add(ChatLine line) {
   lines.Add(line);
   if(lines.Count>200)lines.RemoveAt(0);
   if(LineAdded!=null)LineAdded(line);
   Raise();
  }
  ChatTurn[] BuildTurns(string user,string recalled,string evidence) {
   return session.Build(new ConversationRequest(state,user,retrieveProfile==null?profileDigest():retrieveProfile(user),dsh.PromptContext(),WebApi.PromptContext(state),recalled,evidence,clock.Now));
  }
  void OnFailure(string error) {
   busy=false; pendingBiliUser=""; LastHint=error; LastMeta="未完成";
   Add(new ChatLine(ChatSpeaker.Notice,error,""));
  }
  public void Dispose() {
   work.Dispose(); busy=false;
   dsh.Finished-=OnDshFinished; bili.Finished-=OnBiliFinished;
  }
  void Raise() {if(Changed!=null)Changed();}
 }
}
