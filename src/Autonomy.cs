using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Windows.Threading;

#pragma warning disable 0649
namespace ElymsPet {
 public sealed class AutonomyPlan {
  public string Type="rest",Topic="",Message="",Task="",Note="",Reason="";
 }
 [DataContract] sealed class AutonomyPlanData {
  [DataMember] public string type;
  [DataMember] public string topic;
  [DataMember] public string message;
  [DataMember] public string task;
  [DataMember] public string note;
  [DataMember] public string reason;
 }
 [DataContract] public sealed class AutonomyEntry {
  [DataMember] public DateTime time;
  [DataMember] public string type;
  [DataMember] public string summary;
 }
 [DataContract] public sealed class AutonomyJournal {
  [DataMember] public AutonomyEntry[] entries;
 }
 /// <summary>约每 30 分钟的自主时间：她自己决定聊天、逛 B站、写笔记，或（显式允许时）做一个小实验。</summary>
 public sealed class AutonomyService : IDisposable {
  readonly PetState settings;
  readonly BiliService bili;
  readonly DshWorkbench dsh;
  readonly ChatService conversation;
  readonly Func<string> profileDigest;
  readonly IClock clock;
  readonly WorkScope work;
  readonly Func<AutonomyRequest,AutonomyPlan> planner;
  readonly IRepository<AutonomyJournal> repository;
  readonly DispatcherTimer timer;
  readonly List<AutonomyEntry> journal=new List<AutonomyEntry>();
  readonly Random random;
  DateTime nextAt=DateTime.MinValue;
  bool running,waitingBili,waitingDsh;
  string activity="",lastSummary="",lastType="";
  public event Action Changed;
  public string LastSummary {get {return lastSummary;}}
  public string LastType {get {return lastType;}}
  public DateTime NextAt {get {return nextAt;}}
  public bool Running {get {return running;}}
  public string Status {
   get {
    if(!settings.AutoLife) return "○ 自主活动已关闭";
    if(running) return "◌ "+(string.IsNullOrEmpty(activity)?"正在安排自己的时间…":activity);
    if(nextAt==DateTime.MinValue) return "● 自主活动待命";
    string tail=string.IsNullOrEmpty(lastSummary)?"":" · 上次："+lastSummary;
    return "● 下次约 "+nextAt.ToString("HH:mm")+tail;
   }
  }
  public AutonomyService(PetState settings,IWorkScheduler scheduler,BiliService bili,DshWorkbench dsh,
   ChatService conversation,Func<string> profileDigest,IRepository<AutonomyJournal> repository,IClock clock,Random random,bool preview,Func<AutonomyRequest,AutonomyPlan> planner=null) {
   this.settings=settings;this.bili=bili;this.dsh=dsh;this.conversation=conversation;
   this.planner=planner??AutonomyPlanner.Plan;
   this.profileDigest=profileDigest;this.repository=repository;this.clock=clock;this.random=random;
   work=new WorkScope(scheduler,delegate(string error) {Complete("error",error);},!preview);
   LoadJournal();
   timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(60)};
   timer.Tick+=delegate {Tick();};
   nextAt=clock.Now.AddMinutes(RandomMinutes());
   bili.Finished+=OnBiliFinished;
   dsh.Finished+=OnDshFinished;
   if(!preview)timer.Start();
  }
  public void RunNow() {
   if(work.IsDisposed||running)return;
   nextAt=clock.Now;
   Run();
  }
  int RandomMinutes() {
   int baseMinutes=settings.AutoLifeMinutes>0?settings.AutoLifeMinutes:30;
   return Math.Max(5,baseMinutes+random.Next(-5,6));
  }
  void Tick() {
   if(!settings.AutoLife)return;
   if(work.IsDisposed||running)return;
   DateTime now=clock.Now;
   // 只在真正到点后才因为睡眠或忙碌顺延；否则每分钟的检查会把原定时间
   // 一路提前，最后变成“刚醒就自主”，不再是最初的 25—35 分钟。
   if(now<nextAt)return;
   if(settings.Sleeping||settings.FocusEnd!=DateTime.MinValue||settings.PausedSeconds>0) {
    lastSummary="正在休息或专注，这次先不打扰";
    nextAt=now.AddMinutes(10);Raise();return;
   }
   if(conversation.Busy||bili.Busy||dsh.Busy) {
    lastSummary="现在正忙，等这一阵过去再自己安排";
    nextAt=now.AddMinutes(5);Raise();return;
   }
   Run();
  }
  void Run() {
   if(work.IsDisposed||running)return;
   if(!settings.AutoLife) {Raise();return;}
   if(settings.Sleeping||settings.FocusEnd!=DateTime.MinValue||settings.PausedSeconds>0) {
    nextAt=clock.Now.AddMinutes(10);
    lastSummary="正在休息或专注，这次先不打扰";
    Raise();return;
   }
   if(conversation.Busy||bili.Busy||dsh.Busy) {
    nextAt=clock.Now.AddMinutes(5);
    lastSummary="现在正忙，等这一阵过去再自己安排";
    Raise();return;
   }
   running=true;activity="正在想这段时间做什么…";Raise();
   var request=new AutonomyRequest(settings,ApiKey.Resolve(settings).Key,profileDigest(),
    conversation.RecentContext(8),lastSummary,dsh.PromptContext(),WebApi.PromptContext(settings),clock.Now);
   work.Run(delegate {return planner(request);},delegate(AutonomyPlan plan) {Execute(plan);});
  }
  void Execute(AutonomyPlan plan) {
   // 规划可能耗时数十秒，提交动作之前必须重新检查用户当前的设置和活动。
   if(!AutonomyPolicy.CanExecute(settings,conversation.Busy,bili.Busy,dsh.Busy,work.IsDisposed)) {
    Complete("skipped","状态已变化，这次先不打扰");return;
   }
   if(plan==null)plan=new AutonomyPlan();
   string type=(plan.Type??"rest").ToLowerInvariant();
   if(type=="chat") {
    string message=(plan.Message??"").Trim();
    if(message.Length==0)message="在忙吗？我只是突然想过来打个招呼。";
    conversation.AddAutonomous(message,"自主活动 · 来聊两句");
    Complete("chat","主动找你聊天："+Clip(message,80));
   } else if(type=="bili") {
    string topic=(plan.Topic??"").Trim();
    activity="正在 B站 逛逛…";Raise();
    waitingBili=true;
    if(!bili.Browse(topic,false)&&waitingBili)Complete("bili","B站正忙，这次没有逛成");
   } else if(type=="note") {
    string note=(plan.Note??"").Trim();
    if(note.Length==0)note="今天也在桌面上，把一点小事记下来。";
    conversation.AddAutonomous("我在自己的小本子上写了一段：\n"+note,"自主活动 · 笔记");
    Complete("note","写了一段笔记："+Clip(note,80));
   } else if(type=="dsh") {
    bool dshAllowed=settings.DshEnabled&&settings.AutoLifeDsh&&settings.DshAutoRun;
    if(!dshAllowed) {
     string idea=(plan.Task??plan.Note??"").Trim();
     conversation.AddAutonomous("我有个想做的小东西："+(idea.Length>0?Clip(idea,100):"还没想好具体做什么")+"。不过自主开发还没打开，我先记下来，等你说可以。","自主活动 · 想法");
     Complete("note","记下一个开发想法，等你确认");
     return;
    }
    string task=(plan.Task??"").Trim();
    if(task.Length==0) {Complete("dsh","没有想好要做什么，这次先不动手");return;}
    string prompt="这是曳尔沐的自主时间小实验。只允许在 autonomy/ 目录下创建或修改文件；不要修改 src/、dist/、README、插件或其它现有文件；不要安装系统软件；不要操作桌面 GUI；不要读取或输出 API Key。任务："+task+" 完成后用几句话说明做了什么、文件在哪里。";
    activity="在 DSH 里做一个小实验…";Raise();
    waitingDsh=true;
    if(!dsh.Start(prompt,"workspace-write","曳尔沐的自主小实验")&&waitingDsh)Complete("dsh","DSH 没有接上，这次没有动手");
   } else {
    Complete("rest","这次什么也没做，发了一会儿呆");
   }
  }
  void OnBiliFinished(BiliBrowseResult result) {
   if(!waitingBili)return;
   waitingBili=false;running=false;
   if(result!=null&&result.Ok) {
    string title=result.Selected==null?"":result.Selected.Title;
    conversation.AddAutonomous(result.Summary,"自主活动 · B站逛逛");
    if(result.Selected!=null)conversation.AddAutonomyNotice("她自己停在这里：\n"+title+"\n"+result.Selected.Url);
    Complete("bili","去 B站 逛了《"+Clip(title,40)+"》");
   } else {
    conversation.AddAutonomyNotice("自主时间：B站这次没有逛成"+(result==null?"":"："+result.Error));
    Complete("bili","B站没有逛成");
   }
  }
  void OnDshFinished(DshJob job) {
   if(!waitingDsh)return;
   waitingDsh=false;running=false;
   if(job!=null&&job.Status=="done") {
    conversation.AddAutonomyNotice("自主时间的小实验完成了 · "+(string.IsNullOrEmpty(job.Title)?"DSH 任务":job.Title));
    Complete("dsh","完成了一个 DSH 小实验");
   } else {
    conversation.AddAutonomyNotice("自主时间的小实验没有完成"+(job==null?"":"："+job.Error));
    Complete("dsh","DSH 小实验没有完成");
   }
  }
  void Complete(string type,string summary) {
   running=false;waitingBili=false;waitingDsh=false;activity="";
   lastType=type??"";lastSummary=summary??"";
   nextAt=clock.Now.AddMinutes(RandomMinutes());
   AddJournal(lastType,lastSummary);
   Raise();
  }
  void AddJournal(string type,string summary) {
   journal.Add(new AutonomyEntry {time=clock.Now,type=type??"",summary=summary??""});
   while(journal.Count>30)journal.RemoveAt(0);
   SaveJournal();
  }
  void LoadJournal() {
   AutonomyJournal value=repository.Load();
   if(value==null||value.entries==null)return;
   for(int i=0;i<value.entries.Length;i++)if(value.entries[i]!=null)journal.Add(value.entries[i]);
   while(journal.Count>30)journal.RemoveAt(0);
   if(journal.Count>0) {AutonomyEntry last=journal[journal.Count-1];lastSummary=last.summary??"";lastType=last.type??"";}
  }
  void SaveJournal() {
   if(!repository.Save(new AutonomyJournal {entries=journal.ToArray()}))
    lastSummary+="（活动记录保存失败）";
  }
  static string Clip(string text,int max) {
   text=(text??"").Trim();return text.Length<=max?text:text.Substring(0,max)+"…";
  }
  void Raise() {if(Changed!=null)Changed();}
  public void Dispose() {
   work.Dispose();running=false;waitingBili=false;waitingDsh=false;
   timer.Stop();
   bili.Finished-=OnBiliFinished;
   dsh.Finished-=OnDshFinished;
  }
 }
}
