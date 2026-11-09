using System;
using System.Collections.Generic;
using System.Text;

namespace ElymsPet {
 /// <summary>存档单元的共享逻辑：陪伴面板和浮动输入框读写同一份存档，访谈与整理只在这里跑一份。</summary>
 public sealed class ArchiveService {
  readonly PetWindow pet;
  readonly ArchiveBook book;
  ArchiveSlot slot;
  bool busy;
  string status="",notice="",busyTitle="";
  string pendingAnswer="";
  int turnRevision,profileRevision,slotRevision;
  public event Action Changed;
  public ArchiveService(PetWindow target) {
   pet=target;
   book=pet.IsPreview?ArchiveStore.Demo():ArchiveStore.Load();
   slot=ArchiveStore.Active(book);
   status=slot!=null&&slot.Turns.Count==0?"还没有访谈记录。点「提问」开始，或者直接回答她的问题。":"准备好继续上次的访谈。";
   UpdateDigest();
   if(!pet.IsPreview) ArchiveStore.Save(book);
  }
  public ArchiveBook Book {get {return book;}}
  public ArchiveSlot Slot {get {return slot;}}
  public bool Busy {get {return busy;}}
  public string BusyTitle {get {return busyTitle;}}
  public string Status {get {return status;}}
  public string Notice {get {return notice;}}
  public bool HasQuestion {get {return HasPendingAsk();}}
  public bool CanAsk {get {return HasApi();}}
  public int TurnRevision {get {return turnRevision;}}
  public int ProfileRevision {get {return profileRevision;}}
  public int SlotRevision {get {return slotRevision;}}
  public string LatestQuestion {
   get {
    if(slot==null||slot.Turns.Count==0)return "";
    ArchiveTurn last=slot.Turns[slot.Turns.Count-1];
    return last.Role=="ask"?last.Text:"";
   }
  }
  public void Notify(string text) {status=text==null?"":text;notice="";Raise();}
  public void Save() {
   if(pet.IsPreview) return;
   if(!ArchiveStore.Save(book)) {status="保存失败："+ArchiveStore.LastError;Raise();}
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
   string old=slot.Name;
   ArchiveStore.Delete(book,slot.Id);
   slot=ArchiveStore.Active(book);
   notice="";status="已删除「"+old+"」，当前是「"+slot.Name+"」。";
   Save();TouchAll();return true;
  }
  public bool DeleteItem(ArchiveItem item) {
   if(slot==null||item==null||busy) return false;
   if(!slot.Items.Remove(item)) return false;
   slot.Updated=DateTime.UtcNow;
   notice="已从画像里删掉「"+item.Title+"」。";status=notice;
   Save();profileRevision++;UpdateDigest();Raise();return true;
  }
  public void ClearProfile() {
   if(slot==null||busy||slot.Items.Count==0) return;
   slot.Items.Clear();slot.Summary="";slot.Updated=DateTime.UtcNow;
   notice="画像已清空。";status=notice;
   Save();profileRevision++;UpdateDigest();Raise();
  }
  public void ClearTurns() {
   if(slot==null||busy) return;
   slot.Turns.Clear();slot.QuestionIndex=0;
   notice="访谈记录已清空，画像条目不受影响。";status=notice;
   Save();turnRevision++;Raise();
  }
  public void EnsureQuestion() {
   if(slot==null||busy) return;
   if(HasPendingAsk()) return;
   AskNext();
  }
  public void AskNext() {
   if(slot==null||busy) return;
   if(HasPendingAsk()) {status="上一条问题还没有回答。先回答它，或清空访谈记录重来。";Raise();return;}
   if(!pet.State.AiChat) {status="出题需要接通在线 AI：请在「设置 → AI 对话」里启用在线 AI。";busyTitle="";Raise();return;}
   ApiKey.State key=ApiKey.Resolve(pet.State);
   if(!key.Configured) {status="出题需要 DeepSeek API Key：请在「设置 → AI 对话」里配置。";busyTitle="";Raise();return;}
   ChatTurn[] turns=ArchiveInterview.BuildQuestion(slot);
   string baseUrl=pet.State.ApiBase,model=pet.State.ApiModel;
   busy=true;busyTitle="正在想下一个问题…";status=busyTitle;notice="";
   Raise();
   Ui.InBackground(pet.Dispatcher,delegate {
    return DeepSeek.Complete(key.Key,baseUrl,model,false,turns,30,180,0.8);
   },delegate(ChatOutcome outcome) {FinishQuestion(outcome);});
  }
  void FinishQuestion(ChatOutcome outcome) {
   busy=false;
   busyTitle="";
   if(slot==null) {Raise();return;}
   if(outcome==null||!outcome.Ok) {
    status="出题没有成功："+(outcome==null?"没有接通。":outcome.Error);
    Raise();return;
   }
   string question=ArchiveInterview.CleanQuestion(outcome.Text);
   if(question.Length==0) {status="这次没有生成有效的问题，再试一次。";Raise();return;}
   ArchiveStore.AddTurn(slot,"ask",question);
   notice="";
   status="问题已发出。回答后由 DeepSeek 整理成条目。";
   Save();turnRevision++;Raise();
  }
  public bool Answer(string raw) {
   if(slot==null||busy) return false;
   string text=(raw==null?"":raw).Trim();
   if(text.Length==0) return false;
   if(text.Length>2000) text=text.Substring(0,2000);
   if(!HasPendingAsk()) {status="先让曳尔沐问一个问题，再回答。";Raise();return false;}
   if(pet.State.Sleeping) {pet.State.Sleeping=false;pet.Save();}
   ArchiveStore.AddTurn(slot,"answer",text);
   pendingAnswer=text;notice="";
   status="正在把你的回答整理进存档…";
   turnRevision++;
   if(!pet.State.AiChat) {OfflineInterview(text,"在线 AI 已关闭，这次先离线记录。");return true;}
   ApiKey.State key=ApiKey.Resolve(pet.State);
   if(!key.Configured) {OfflineInterview(text,"还没有配置 API Key，这次先离线记录。");return true;}
   ChatTurn[] turns=ArchiveInterview.BuildTurn(slot);
   string baseUrl=pet.State.ApiBase,model=pet.State.ApiModel;
   busy=true;busyTitle="正在整理这条回答…";
   Save();Raise();
   pet.Act(6,"正在整理这条知识，稍等一下…",20);
   Ui.InBackground(pet.Dispatcher,delegate {
    return DeepSeek.Complete(key.Key,baseUrl,model,false,turns,60,1400,0.4);
   },delegate(ChatOutcome outcome) {FinishInterview(outcome);});
   return true;
  }
  public void ExtractRecent() {
   if(slot==null||busy) return;
   if(slot.Turns.Count==0) {status="还没有访谈内容可以整理。";Raise();return;}
   if(!HasApi()) {status="离线模式只能逐条记录；接入 API 后可以把访谈重新整理成条目。";Raise();return;}
   ApiKey.State key=ApiKey.Resolve(pet.State);
   ChatTurn[] turns=ArchiveInterview.BuildExtract(slot);
   string baseUrl=pet.State.ApiBase,model=pet.State.ApiModel;
   busy=true;busyTitle="正在重新整理最近的访谈…";status=busyTitle;notice="";Raise();
   Ui.InBackground(pet.Dispatcher,delegate {
    return DeepSeek.Complete(key.Key,baseUrl,model,false,turns,60,1800,0.2);
   },delegate(ChatOutcome outcome) {FinishExtract(outcome);});
  }
  void FinishInterview(ChatOutcome outcome) {
   busy=false;busyTitle="";
   if(slot==null) {Raise();return;}
   if(outcome==null||!outcome.Ok) {
    OfflineInterview(pendingAnswer,outcome==null?"这次没有接通。":"这次没有接通。"+outcome.Error);
    return;
   }
   InterviewResult result=ArchiveInterview.Parse(outcome.Text);
   if(result.Structured) {
    string nextQuestion=ArchiveInterview.CleanQuestion(result.Reply);
    bool asked=nextQuestion.Length>0&&ArchiveInterview.LooksLikeQuestion(nextQuestion);
    if(asked) ArchiveStore.AddTurn(slot,"ask",nextQuestion);
    int added=ArchiveStore.Merge(slot,result.Items);
    if(result.Summary.Length>0) slot.Summary=result.Summary;
    status="本轮新增 "+added+" 条 · "+outcome.Seconds.ToString("0.0")+"s · "+outcome.CompletionTokens+" tokens";
    notice=Describe(result.Items,added);
    Save();turnRevision++;profileRevision++;UpdateDigest();Raise();
    if(added>0) pet.Act(4,"收到一条新知识，已经写进你的画像里了。",6);
    else pet.Act(6,"这条我记下了。再讲具体一点，我就能整理成条目。",5);
    if(!asked) AskNext();
    return;
   }
   // 模型没有按 JSON 回复：对话照常保留，再单独跑一次整理，避免这一轮白聊。
   string unstructuredQuestion=ArchiveInterview.CleanQuestion(result.Reply);
   bool unstructuredAsked=unstructuredQuestion.Length>0&&ArchiveInterview.LooksLikeQuestion(unstructuredQuestion);
   if(unstructuredAsked) ArchiveStore.AddTurn(slot,"ask",unstructuredQuestion);
   notice="";status="这轮回复不是结构化格式，正在单独整理条目…";
   Save();turnRevision++;Raise();
   ApiKey.State key=ApiKey.Resolve(pet.State);
   if(!key.Configured) {busy=false;busyTitle="";status="这轮没能整理成条目；接入 API 后可以重新整理。";Raise();return;}
   ChatTurn[] turns=ArchiveInterview.BuildExtract(slot);
   string baseUrl=pet.State.ApiBase,model=pet.State.ApiModel;
   busy=true;busyTitle="正在单独整理条目…";Raise();
   Ui.InBackground(pet.Dispatcher,delegate {
    return DeepSeek.Complete(key.Key,baseUrl,model,false,turns,60,1800,0.2);
   },delegate(ChatOutcome extract) {FinishExtract(extract);});
  }
  void FinishExtract(ChatOutcome outcome) {
   busy=false;busyTitle="";
   if(slot==null) {Raise();return;}
   if(outcome==null||!outcome.Ok) {status="整理没有成功："+(outcome==null?"没有接通。":outcome.Error);Raise();return;}
   InterviewResult result=ArchiveInterview.Parse(outcome.Text);
   if(!result.Structured) {status="这批内容没能整理成条目，稍后再试一次。";Raise();return;}
   int added=ArchiveStore.Merge(slot,result.Items);
   if(result.Summary.Length>0) slot.Summary=result.Summary;
   status="整理完成：新增 "+added+" 条，现有 "+slot.Items.Count+" 条 · "+outcome.Seconds.ToString("0.0")+"s";
   notice=Describe(result.Items,added);
   bool needQuestion=!HasPendingAsk();
   Save();profileRevision++;UpdateDigest();Raise();
   if(added>0) pet.Act(4,"重新整理好了，画像又清楚了一点。",6);
   if(needQuestion) AskNext();
  }
  void OfflineInterview(string answer,string hint) {
   busy=false;busyTitle="";
   if(slot==null) {Raise();return;}
   string asked=ArchiveGuide.LastAsk(slot);
   GuideQuestion question=ArchiveGuide.ForAsk(asked);
   bool addedSomething=false;
   string note="";
   if(question!=null&&question.Summary) {
    if(answer!=null&&answer.Trim().Length>0) {slot.Summary=ChatText.Clip(answer,160);note="画像摘要已更新。";}
   } else {
    ArchiveItem item=ArchiveGuide.FromAnswer(question,answer);
    var items=new List<ArchiveItem>();
    if(item!=null) items.Add(item);
    int added=ArchiveStore.Merge(slot,items);
    addedSomething=added>0;
    note=addedSomething?"已在本地记录 1 条画像条目。":"这条内容没有新增条目。";
   }
   status=hint+" "+note+"（已离线记录；接上 API 后可以继续出题）";
   notice=addedSomething?"离线记下 1 条，等线路稳了再整理。":"这条没有新增条目。";
   Save();turnRevision++;profileRevision++;UpdateDigest();Raise();
   if(addedSomething) pet.Act(4,"这条先记在本机。等线路稳了，我再帮你整理。",6);
  }
  bool HasApi() {return pet.State.AiChat&&ApiKey.Resolve(pet.State).Configured;}
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
  void UpdateDigest() {pet.ProfileDigest=slot==null?"":ArchiveStore.Digest(slot,10,340);}
  void Raise() {if(Changed!=null) Changed();}
 }
}
