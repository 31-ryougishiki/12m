using System;
using System.Collections.Generic;
using System.Text;

namespace ElymsPet {
 /// <summary>存档单元的共享逻辑：陪伴面板和浮动输入框读写同一份存档，访谈与整理只在这里跑一份。</summary>
 public sealed partial class ArchiveService : IDisposable {
  readonly PetState state;
  readonly bool preview;
  readonly PetFeedback feedback;
  readonly WorkScope work;
  readonly IWorkScheduler sourceScheduler;
  readonly IRepository<ArchiveBook> repository;
  readonly IArchiveApi api;
  public string Digest {get;private set;}
  ArchiveBook book;
  ArchiveSlot slot;
  bool busy;
  string status="",notice="",busyTitle="";
  string pendingAnswer="";
  int turnRevision,profileRevision,slotRevision;
  public event Action Changed;
  public ArchiveService(PetState state,IWorkScheduler scheduler,PetFeedback feedback,IRepository<ArchiveBook> repository,bool preview,IArchiveApi api=null,IArchiveCloudClient cloudApi=null) {
   this.api=api??new ArchiveApi();
   sourceScheduler=scheduler;
   this.preview=preview;this.state=state;this.feedback=feedback;this.repository=repository;
   work=new WorkScope(scheduler,delegate(string error) {busy=false;busyTitle="";status=error+" 原话已保留，点「继续 / 重试」重试当前阶段。";Raise();},!preview);
   book=ArchiveStore.Normalize(repository.Load());
   slot=ArchiveStore.Active(book);
   status=slot!=null&&slot.Turns.Count==0?"填写流程名逐项补充，或填写主题后点「聊新主题」。":"准备好继续上次的访谈。";
   UpdateDigest();
   Save();
   InitializeCloud(scheduler,cloudApi);
   InitializeGraphEditing();
  }
  public ArchiveBook Book {get {return book;}}
  public ArchiveSlot Slot {get {return slot;}}
  public bool Busy {get {return busy;}}
  public string BusyTitle {get {return busyTitle;}}
  public string Status {get {return status;}}
  public string Notice {get {return notice;}}
  public bool HasQuestion {get {return ReviewingWorkflow?!ReviewWaiting:HasPendingAsk();}}
  public bool CanAsk {get {return HasApi();}}
  public string ResumeSummary {get {return ArchiveResearch.ResumeSummary(slot);}}
  public string ResearchStatus {get {return "整理与追问：深度思考已开启 · "+(state.WebEnabled?"联网查证已开启":"联网查证已关闭（可在设置中开启）");}}
  public string LatestSources {get {return HasPendingAsk()?slot.Turns[slot.Turns.Count-1].Sources??"":"";}}
  public string LatestAssessment {get {return HasPendingAsk()?slot.Turns[slot.Turns.Count-1].Assessment??"":"";}}
  public int TurnRevision {get {return turnRevision;}}
  public int ProfileRevision {get {return profileRevision;}}
  public int SlotRevision {get {return slotRevision;}}
  public string LatestQuestion {
   get {
    if(ReviewingWorkflow)return ReviewQuestion;
    if(slot==null||slot.Turns.Count==0)return "";
    ArchiveTurn last=slot.Turns[slot.Turns.Count-1];
    return last.Role=="ask"?last.Text:"";
   }
  }
  public void Notify(string text) {status=text==null?"":text;notice="";Raise();}
  public bool Save() {
   if(!repository.Save(ArchiveStore.Normalize(book))) {status="保存失败："+repository.LastError;Raise();return false;}
   if(!preview&&slot!=null)try {ArchiveKnowledge.WriteFiles(slot);}catch(Exception e){status="存档已保存，但知识库文件同步失败，可重试："+e.Message;Raise();}
   return true;
  }
  public bool Select(ArchiveSlot target) {
   if(target==null||busy) return false;
   if(target==slot) {status="当前已经是「"+slot.Name+"」。";Raise();return false;}
   slot=target;book.ActiveId=target.Id;
   notice="";status="已切换到「"+slot.Name+"」。";
   Save();TouchAll();return true;
  }
  public ArchiveSlot Create(string name) {
   if(busy) return slot;
   if(string.IsNullOrEmpty((name==null?"":name).Trim())) name="存档 "+(book.Slots.Count+1);
   slot=ArchiveStore.Create(book,name);
   notice="";status="已新建存档「"+slot.Name+"」。";
   Save();TouchAll();return slot;
  }
  public bool Rename(string name) {
   if(slot==null||busy) return false;
   if(!ArchiveStore.Rename(book,slot.Id,name)) {status="改名没有成功，输入的名字可能是空的。";Raise();return false;}
   notice="";status="已改名为「"+slot.Name+"」。";
   Save();slotRevision++;Raise();return true;
  }
  public bool Delete() {
   if(slot==null||busy) return false;
   if(book.Slots.Count<=1) {status="至少保留一个存档。";Raise();return false;}
   string old=slot.Name;var removed=slot;
   ArchiveStore.Delete(book,slot.Id);
   slot=ArchiveStore.Active(book);
   notice="";status="已删除「"+old+"」，当前是「"+slot.Name+"」。";
   if(Save()&&!preview)try {ArchiveKnowledge.RemoveFiles(removed);}catch(Exception e){status="存档已删除，但派生文件清理失败："+e.Message;}
   TouchAll();return true;
  }
  public bool DeleteItem(ArchiveItem item) {
   if(slot==null||item==null||busy) return false;
   if(!slot.Items.Remove(item)) return false;
   slot.Changes.RemoveAll(delegate(ArchiveChange c){return (c.Before!=null&&c.Before.Id==item.Id)||(c.After!=null&&c.After.Id==item.Id);});
   slot.Updated=DateTime.UtcNow;
   notice="已从画像里删掉「"+item.Title+"」。";status=notice;
   Save();profileRevision++;UpdateDigest();Raise();return true;
  }
  public void ClearProfile() {
   if(slot==null||busy||slot.Items.Count==0) return;
   slot.Items.Clear();slot.Changes.Clear();slot.Summary="";slot.Updated=DateTime.UtcNow;
   notice="画像已清空。";status=notice;
   Save();profileRevision++;UpdateDigest();Raise();
  }
  public void ClearTurns() {
   if(slot==null||busy) return;
   slot.Turns.Clear();slot.QuestionIndex=0;slot.InterviewStage="";
   notice="访谈记录已清空，画像条目不受影响。";status=notice;
   Save();turnRevision++;Raise();
  }
  public void EnsureQuestion() {
   if(!PluginAccess.Enabled(state,"archive")){Notify("请先安装并启用存档单元插件。");return;}
   if(slot==null||busy) return;
   if(HasPendingAsk()||slot.Paused) return;
   ResumeInterview();
  }
  public void ResumeInterview() {
   if(!PluginAccess.Enabled(state,"archive")){Notify("请先安装并启用存档单元插件。");return;}
   if(slot==null||busy||work.IsDisposed)return;
   if(ReviewingWorkflow){ResumeWorkflowReview();return;}
   slot.Paused=false;
   if(!Save())return;
   if(HasPendingAsk()){status="已恢复上次聊天，保留原来的问题。回答后会按当前联网设置继续查证和追问。";Raise();return;}
   if(slot.Turns.Count>0&&slot.InterviewStage!="question")ExtractRecent(true);else AskNext();
  }
  public void AskNext() {
   if(!PluginAccess.Enabled(state,"archive")){Notify("请先安装并启用存档单元插件。");return;}
   if(ReviewingWorkflow){if(!busy)ResumeWorkflowReview();return;}
   if(slot==null||busy||work.IsDisposed) return;
   slot.Paused=false;
   if(HasPendingAsk()) {Save();status="上一条问题还没有回答。可以继续回答，也可以跳过或切换主题。";Raise();return;}
   if(!state.AiChat) {status="出题需要接通在线 AI：请在「设置 → AI 对话」里启用在线 AI。";busyTitle="";Raise();return;}
   ApiKey.State key=ApiKey.Resolve(state);
   if(!key.Configured) {status="出题需要 DeepSeek API Key：请在「设置 → AI 对话」里配置。";busyTitle="";Raise();return;}
   ChatTurn[] turns=ArchiveInterview.BuildQuestion(slot);
   PetState snapshot=state.Snapshot();
   string context=ArchiveResearch.Context(slot);var urls=ArchiveResearch.ProvidedUrls(slot);
   slot.InterviewStage="question";
   busy=true;busyTitle=state.WebEnabled?"正在查证资料并深入思考下一个问题…":"正在深入思考下一个问题…";status=busyTitle;notice="";
   Save();Raise();
   work.Run(delegate {
    return ArchiveResearch.Ask(api,snapshot,key.Key,turns,context,urls);
   },delegate(ArchiveResearchResult result) {
    ArchiveResearch.Merge(slot,result);profileRevision++;
    FinishQuestion(result.Question,result.Sources);
   });
  }
  void FinishQuestion(ChatOutcome outcome,string sources) {
   busy=false;
   busyTitle="";
   if(slot==null) {Raise();return;}
   if(outcome==null||!outcome.Ok) {
    status="出题没有成功："+(outcome==null?"没有接通。":outcome.Error)+" 原话与整理结果已保留，点「继续 / 重试」只重试提问。";
    Save();Raise();return;
   }
   if(outcome.Truncated){status="思考或追问输出未完整结束，请重试；当前访谈和已取回资料已保留。";Save();Raise();return;}
   string question=ArchiveInterview.CleanQuestion(outcome.Text);
   var assessment=ArchiveAssessment.Parse(outcome.Text);
   if(question.Length==0||assessment==null) {status="这次没有生成完整的协助判断，点「继续 / 重试」重试。";Save();Raise();return;}
   ArchiveStore.AddTurn(slot,"ask",question);
   slot.InterviewStage="";
   slot.Turns[slot.Turns.Count-1].Sources=sources;
   slot.Turns[slot.Turns.Count-1].Assessment=assessment.Display();
   notice="";
   status="问题已发出 · "+(outcome.HasReasoning?"模型已返回思考结果":"已请求模型深度思考")+"。回答后继续整理。";
   Save();turnRevision++;Raise();
  }
  public bool Answer(string raw) {
   if(!PluginAccess.Enabled(state,"archive")){Notify("请先安装并启用存档单元插件。");return false;}
   if(slot==null||busy||work.IsDisposed) return false;
   if(ReviewingWorkflow)return AnswerWorkflowUnit(raw);
   string text=(raw==null?"":raw).Trim();
   if(text.Length==0) return false;
   if(slot.Paused){Notify("访谈已暂停，请先继续访谈。");return false;}
   if(text.Length>2000) text=text.Substring(0,2000);
   if(!HasPendingAsk()) {status="先让曳尔沐问一个问题，再回答。";Raise();return false;}
   feedback.Wake();
   ArchiveStore.AddTurn(slot,"answer",text);
   slot.InterviewStage="extract";
   pendingAnswer=text;notice="";
   status="正在把你的回答整理进存档…";
   turnRevision++;
   if(!state.AiChat) {OfflineInterview(text,"在线 AI 已关闭，这次先离线记录。");return true;}
   ApiKey.State key=ApiKey.Resolve(state);
   if(!key.Configured) {OfflineInterview(text,"还没有配置 API Key，这次先离线记录。");return true;}
   ChatTurn[] turns=ArchiveInterview.BuildTurn(slot);
   string baseUrl=state.ApiBase,model=state.ApiModel;
   busy=true;busyTitle="正在思考你的回答，核实知识边界与流程…";
   Save();Raise();
   feedback.Act(6,"正在整理这条知识，稍等一下…",20);
   work.Run(delegate {
    return ArchiveCompletion.Complete(api,key.Key,baseUrl,model,turns,16384,0.3,true);
   },delegate(ChatOutcome outcome) {FinishInterview(outcome);});
   return true;
  }
  public void ExtractRecent(bool resume=false) {
   if(!PluginAccess.Enabled(state,"archive")){Notify("请先安装并启用存档单元插件。");return;}
   if(slot==null||busy||work.IsDisposed) return;
   if(slot.Turns.Count==0) {status="还没有访谈内容可以整理。";Raise();return;}
   if(!HasApi()) {status="离线模式只能逐条记录；接入 API 后可以把访谈重新整理成条目。";Raise();return;}
   ApiKey.State key=ApiKey.Resolve(state);
   ChatTurn[] turns=ArchiveInterview.BuildExtract(slot);
   if(resume)turns[0]=new ChatTurn("system",turns[0].Content+"\n用户刚刚明确点击了恢复上次聊天。沿用当前主题，忽略历史中的暂停请求，本次 pause=false；不要重置背景。");
   string baseUrl=state.ApiBase,model=state.ApiModel;
   slot.InterviewStage="extract";
   busy=true;busyTitle="正在深入思考并重新整理最近的访谈…";status=busyTitle;notice="";Save();Raise();
   work.Run(delegate {
    return ArchiveCompletion.Complete(api,key.Key,baseUrl,model,turns,16384,0.2,true);
   },delegate(ChatOutcome outcome) {FinishExtract(outcome,resume);});
  }
  void FinishInterview(ChatOutcome outcome) {
   busy=false;busyTitle="";
   if(slot==null) {Raise();return;}
   if(outcome==null||!outcome.Ok) {
    OfflineInterview(pendingAnswer,outcome==null?"这次没有接通。":"这次没有接通。"+outcome.Error);
    return;
   }
   if(outcome.Truncated){OfflineInterview(pendingAnswer,"思考或整理输出未完整结束，未更新知识条目。");return;}
   InterviewResult result=ArchiveInterview.Parse(outcome.Text);
   if(result.Structured) {
    string nextQuestion=ArchiveInterview.CleanQuestion(result.Reply);
    var assessment=ArchiveAssessment.Parse(outcome.Text);
    bool asked=!state.WebEnabled&&!result.Pause&&assessment!=null&&nextQuestion.Length>0&&ArchiveInterview.LooksLikeQuestion(nextQuestion);
    if(asked) {ArchiveStore.AddTurn(slot,"ask",nextQuestion);slot.Turns[slot.Turns.Count-1].Assessment=assessment.Display();}
    slot.InterviewStage=asked?"":"question";
    ArchiveKnowledge.Merge(slot,result);
    int added=ArchiveStore.Merge(slot,result.Items);
    KnowledgeGraph.Merge(slot,result.Links);
    if(result.Summary.Length>0) slot.Summary=result.Summary;
    status="本轮新增 "+added+" 条 · "+outcome.Seconds.ToString("0.0")+"s · "+outcome.CompletionTokens+" tokens";
    notice=Describe(result.Items,added)+(result.Episodes.Count>0?" · 案例仅在原话依据匹配时收录":"");
    Save();turnRevision++;profileRevision++;UpdateDigest();Raise();
    if(added>0) feedback.Act(4,"收到一条新知识，已经写进你的画像里了。",6);
    else feedback.Act(6,"这条我记下了。再讲具体一点，我就能整理成条目。",5);
    if(!asked&&!slot.Paused) AskNext();
    return;
   }
   OfflineInterview(pendingAnswer,"整理结果格式无效，未更新知识条目。");
  }
  void FinishExtract(ChatOutcome outcome,bool resume=false) {
   busy=false;busyTitle="";
   if(slot==null) {Raise();return;}
   if(outcome==null||!outcome.Ok) {status="整理没有成功："+(outcome==null?"没有接通。":outcome.Error)+" 原话已保留，点「继续 / 重试」重试。";Raise();return;}
   if(outcome.Truncated){status="思考或整理输出未完整结束，原记录已保留，请重试整理。";Raise();return;}
   InterviewResult result=ArchiveInterview.Parse(outcome.Text);
   if(!result.Structured) {status="这批内容没能整理成条目，稍后再试一次。";Raise();return;}
   // Bulk extraction must not replay a pause request from older turns.
   result.Pause=false;
   ArchiveKnowledge.Merge(slot,result);
    int added=ArchiveStore.Merge(slot,result.Items);
    KnowledgeGraph.Merge(slot,result.Links);
   if(result.Summary.Length>0) slot.Summary=result.Summary;
   status="整理完成：新增 "+added+" 条，现有 "+slot.Items.Count+" 条 · "+outcome.Seconds.ToString("0.0")+"s";
   notice=Describe(result.Items,added)+(result.Episodes.Count>0?" · 案例仅在原话依据匹配时收录":"");
   bool needQuestion=!HasPendingAsk();
   slot.InterviewStage=needQuestion?"question":"";
   Save();profileRevision++;UpdateDigest();Raise();
   if(added>0) feedback.Act(4,"重新整理好了，画像又清楚了一点。",6);
   if(needQuestion&&!slot.Paused) AskNext();
  }
  void OfflineInterview(string answer,string hint) {
   busy=false;busyTitle="";
   if(slot==null) {Raise();return;}
   status=hint+" 回答已保存在本地，点「继续 / 重试」重试整理并继续提问；暂不推断掌握程度。";
   notice="原话已保留，等待整理。";Save();turnRevision++;Raise();
  }
  public bool SaveKnowledgeNode(string id,string title,string domain,string boundary,string level,string evidence,string note) {
   if(busy||slot==null)return false;
   var item=ArchiveStore.Sanitize(new ArchiveItem{Title=title,Domain=domain,Boundary=boundary,Level=level,Evidence=evidence,Note=note,Source="用户手动录入"});
   if(item==null){Notify("请填写知识点名称。");return false;}
   var current=slot.Items.Find(delegate(ArchiveItem x){return x.Id==id;});
   if(slot.Items.Exists(delegate(ArchiveItem x){return x.Id!=id&&string.Equals(x.Title,item.Title,StringComparison.OrdinalIgnoreCase)&&string.Equals(x.Domain,item.Domain,StringComparison.OrdinalIgnoreCase);})) {Notify("该领域已有同名知识点，请选择已有节点编辑。");return false;}
   if(current==null) {
    if(slot.Items.Count>=ArchiveStore.MaxItems){Notify("知识点已达上限，请先整理已有记录。");return false;}
    slot.Items.Add(item);ArchiveLearning.Record(slot,"ADD",null,item);
   } else {
    item.Id=current.Id;
    if(ArchiveLearning.Same(current,item)){Notify("内容没有变化。");return true;}
    if(current.Title!=item.Title||current.Domain!=item.Domain)foreach(var link in slot.Links)if(link.From==id||link.To==id)link.Confirmed=false;
    ArchiveLearning.Record(slot,"UPDATE",current,item);slot.Items[slot.Items.IndexOf(current)]=item;
   }
   slot.Updated=DateTime.UtcNow;status="知识节点已保存。";Save();profileRevision++;UpdateDigest();Raise();return true;
  }
  public bool SaveKnowledgeLink(string from,string to,string kind,string evidence) {
   if(busy||slot==null)return false;
   KnowledgeGraph.Normalize(slot);
   if(from==to||!slot.Items.Exists(delegate(ArchiveItem x){return x.Id==from;})||!slot.Items.Exists(delegate(ArchiveItem x){return x.Id==to;})||!KnowledgeGraph.ValidKind(kind)){Notify("请选择两个不同的知识点及关系类型。");return false;}
   evidence=ArchiveStore.Clean(evidence);
   if(evidence.Length==0){Notify("请写下为什么这样关联，便于之后复盘。");return false;}
   var link=KnowledgeGraph.Find(slot,from,to,kind);
   if(link==null){if(slot.Links.Count>=1000){Notify("关系已达上限，请先整理已有关系。");return false;}link=new KnowledgeLink{Id=Guid.NewGuid().ToString("N"),From=from,To=to,Kind=kind};slot.Links.Add(link);}
   link.Evidence=ArchiveStore.Clip(evidence,500);link.Confirmed=true;link.Source="手动关联";link.At=DateTime.UtcNow;
   GraphChanged("关系已保存并由你确认。");return true;
  }
  public void ConfirmKnowledgeLink(KnowledgeLink link) {
   if(busy||slot==null||!slot.Links.Contains(link))return;
   link.Confirmed=!link.Confirmed;GraphChanged(link.Confirmed?"关系已确认。":"关系已退回待核实。");
  }
  public void DeleteKnowledgeLink(KnowledgeLink link) {
   if(busy||slot==null||!slot.Links.Remove(link))return;GraphChanged("关系已删除，知识点保留。");
  }
  void GraphChanged(string message) {slot.Updated=DateTime.UtcNow;status=message;Save();profileRevision++;UpdateDigest();Raise();}
  public string Recall(string query) {return ArchiveRecall.Build(slot,query,14000);}
  public bool RestoreChange(ArchiveChange change) {
   if(busy||slot==null||change==null||change.Before==null||change.After==null||!slot.Changes.Contains(change))return false;
   var current=slot.Items.Find(delegate(ArchiveItem x){return x.Id==change.After.Id;});
   if(current==null||!ArchiveLearning.Same(current,change.After)){Notify("该知识后来已有变化，不能覆盖。请通过纠正访谈确认当前情况。");return false;}
   var restored=ArchiveLearning.Copy(change.Before);restored.At=DateTime.UtcNow;
   if(current.Title!=restored.Title||current.Domain!=restored.Domain)foreach(var link in slot.Links)if(link.From==current.Id||link.To==current.Id)link.Confirmed=false;
   ArchiveLearning.Record(slot,"RESTORE",current,restored);slot.Items[slot.Items.IndexOf(current)]=restored;
   slot.Updated=DateTime.UtcNow;status="已恢复这次修改前的知识记录。";
   Save();profileRevision++;UpdateDigest();Raise();return true;
  }
  public bool RecordFeedback(ArchiveWorkflow workflow,string outcome,string note) {
   if(busy||slot==null)return false;
   if(!ArchiveLearning.Feedback(slot,workflow,outcome,note)){Notify("请写下具体结果、遇到的问题或改进经验。");return false;}
   status=outcome=="失败"?"失败经验已保存，流程退回待核实；可以继续访谈修订。":"复盘已保存，后续相关任务会参考案例；不会自动确认流程。";
   Save();profileRevision++;UpdateDigest();Raise();return true;
  }
  public void RemoveEpisode(ArchiveEpisode episode) {
   if(busy||slot==null||!slot.Episodes.Remove(episode))return;
   slot.Updated=DateTime.UtcNow;Save();profileRevision++;Raise();
  }
  public void Pause() {
   if(busy||slot==null)return;slot.Paused=true;status="访谈已暂停，已整理的内容会保留。";Save();Raise();
  }
  public void Focus(string topic) {
   if(!PluginAccess.Enabled(state,"archive")){Notify("请先安装并启用存档单元插件。");return;}
   if(busy||slot==null)return;
   if(ReviewWaiting){Notify("请先完成待整理的流程补充，或结束本轮。");return;}
   slot.Review=null;
   slot.Focus=ArchiveStore.Clip(ArchiveStore.Clean(topic),100);slot.Paused=false;
   slot.InterviewGoal=slot.Focus;
   if(HasPendingAsk())ArchiveStore.AddTurn(slot,"answer","先跳过这个问题，接下来聚焦："+slot.Focus);
   Save();turnRevision++;Raise();AskNext();
  }
  public void BackToWorkflow() {
   if(busy||slot==null)return;
   string goal=string.IsNullOrEmpty(slot.InterviewGoal)?slot.Focus:slot.InterviewGoal;
   slot.Paused=false;
   ArchiveStore.AddTurn(slot,"answer","先跳过当前问题，回到工作主线："+(string.IsNullOrEmpty(goal)?"最近一项希望你协助的工作":goal)+"。请基于已有信息重新判断你现在能帮我做什么，还缺少哪个最关键的信息；不要重复问已说明的内容。");
   Save();turnRevision++;Raise();AskNext();
  }
  public bool ConfirmWorkflow(ArchiveWorkflow workflow) {
   if(busy||slot==null||!slot.Workflows.Contains(workflow))return false;
   if(!ArchiveKnowledge.Complete(workflow)){Notify("请先通过访谈补齐触发、前置条件、步骤、验收和异常处理，再确认流程。");return false;}
   workflow.Confirmed=!workflow.Confirmed;slot.Updated=DateTime.UtcNow;
   status=workflow.Confirmed?"流程已确认，后续相关工作会主动参考。":"流程已退回草稿。";
   Save();profileRevision++;UpdateDigest();Raise();return true;
  }
  public void FetchResource(ArchiveResource resource) {
   if(busy||slot==null||!slot.Resources.Contains(resource))return;
   if(resource.Url.Length==0){Notify("先在访谈中补充资料链接。");return;}
   if(preview){Notify("预览模式不读取远程资料。");return;}
   if(!state.WebEnabled){Notify("联网已关闭，请先在设置中开启。");return;}
   if(!HasApi()){Notify("读取资料需要先配置在线 API。");return;}
   var key=ApiKey.Resolve(state);string baseUrl=state.ApiBase;
   busy=true;busyTitle="正在读取资料正文…";status=busyTitle;Raise();
   work.Run(delegate {return WebApi.Fetch(key.Key,baseUrl,resource.Url,45);},delegate(WebOutcome outcome){
    busy=false;busyTitle="";
    if(outcome==null||!outcome.Ok){Notify("资料读取失败："+(outcome==null?"无响应":outcome.Error));return;}
    resource.Content=outcome.Content;resource.Retrieved=DateTime.UtcNow;
    DeepSeekShare.Cache(slot,outcome);
    status="可读正文已缓存到本地资料库（最多保存 20000 字符，不代表完整仓库或论文全文）。"+outcome.AttachmentStatus;
    Save();profileRevision++;Raise();
   });
  }
  public void RemoveResource(ArchiveResource resource) {
   if(busy||slot==null)return;slot.Resources.Remove(resource);Save();profileRevision++;Raise();
  }
  public void RemoveWorkflow(ArchiveWorkflow workflow) {
   if(busy||slot==null)return;slot.Workflows.Remove(workflow);Save();profileRevision++;UpdateDigest();Raise();
  }
  bool HasApi() {return state.AiChat&&ApiKey.Resolve(state).Configured;}
  bool HasPendingAsk() {return slot!=null&&slot.Turns.Count>0&&slot.Turns[slot.Turns.Count-1].Role=="ask";}
  string Describe(List<ArchiveItem> items,int added) {
   if(added<=0) return "这条没有新增条目。再讲具体一点，就能整理成条目。";
   if(items==null||items.Count==0) return "已整理 "+added+" 条画像。";
   var text=new StringBuilder("已记下：");
   int shown=Math.Min(added,3);
   shown=Math.Min(shown,items.Count);
   for(int i=0;i<shown;i++) {
    if(i>0) text.Append("、");
    text.Append(items[i].Title).Append("（").Append(items[i].Level).Append("）");
   }
   if(added>shown) text.Append(" 等 ").Append(added).Append(" 条");
   return text.ToString();
  }
  void TouchAll() {slotRevision++;turnRevision++;profileRevision++;UpdateDigest();Raise();}
  void UpdateDigest() {Digest=slot==null?"":ArchiveStore.Digest(slot,40,16000);}
  public void Dispose() {if(graphEditTimer!=null)graphEditTimer.Stop();if(cloudTimer!=null)cloudTimer.Stop();if(cloudWork!=null)cloudWork.Dispose();work.Dispose();busy=false;}
  void Raise() {if(Changed!=null) Changed();}
 }
}
