using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ElymsPet {
 static class RegressionTests {
  static int checks;
  static void Check(bool value,string name) {
   if(!value)throw new Exception(name);
   checks++;Console.WriteLine("PASS " + name);
  }
  [STAThread] static int Main() {
   string root=Path.Combine(Path.GetTempPath(),"ElymsRegression-"+Guid.NewGuid().ToString("N"));
   string originalFolder=Storage.Folder;
   Directory.CreateDirectory(root);
   try {
    if(SelfTest.Run()!=0)throw new Exception(File.ReadAllText("tests/results.txt"));
    Console.WriteLine("PASS legacy: 101 checks");
    ArchiveKnowledgeTests.Run(root);
    ArchiveLearningTests.Run(root);
    KnowledgeGraphTests.Run(root);
    ArchiveResearchTests.Run(root);
    MemoryRoundTrip(root);
    ConversationInputs(root);
    ChatCopyAffordance();
    PopupBehavior();
    PersonaIntegration();
    ModelResponseMetadata();
    MarkerTolerance();
    WorkLifecycle();
    AutonomyEligibility();
    AutonomyLateCompletion(root);
    AutonomySchedule(root);
    AiGating();
    StatusUpdates();
    PreviewIsolation(root);
    ConcurrentPersistence(root);
    PopupRenderingTests.Run(root);
    Console.WriteLine("PASS regressions: "+checks+" checks");
    File.AppendAllText("tests/results.txt","PASS regressions: "+checks+" checks\r\n");
    return 0;
   } catch(Exception error) {
    Console.Error.WriteLine(error);return 1;
   } finally {Storage.Folder=originalFolder;Directory.Delete(root,true);}
  }
  static IEnumerable<Button> DialogButtons(DependencyObject parent) {
   foreach(object child in LogicalTreeHelper.GetChildren(parent)) {
    var button=child as Button;if(button!=null)yield return button;
    var node=child as DependencyObject;if(node!=null)foreach(var nested in DialogButtons(node))yield return nested;
   }
  }
  static void PopupBehavior() {
   var field=new TextBox{Text="sample",IsReadOnly=true};var menu=MenuTheme.EditMenu(field);
   Check(menu.Items.Count==3&&((MenuItem)menu.Items[0]).Command==System.Windows.Input.ApplicationCommands.Copy,"read-only edit menu exposes copy and selection without editing actions");
   var type=typeof(PopupDialog).Assembly.GetType("ElymsPet.PromptWindow");
   var prompt=(Window)Activator.CreateInstance(type,new object[]{"test","confirmation",MessageBoxButton.YesNo,MessageBoxImage.Warning});
   Check((MessageBoxResult)type.GetProperty("Outcome").GetValue(prompt,null)==MessageBoxResult.No,"closing a confirmation never grants permission");
   Button accept=null;bool safeDefault=false;
   foreach(var button in DialogButtons(prompt)) {
    if((string)button.Content=="确认")accept=button;
    if((string)button.Content=="取消")safeDefault=button.IsDefault;
   }
   Check(accept!=null&&!accept.IsDefault&&safeDefault,"confirmation defaults to cancel instead of the destructive action");
   accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   Check((MessageBoxResult)type.GetProperty("Outcome").GetValue(prompt,null)==MessageBoxResult.Yes,"explicit confirmation retains the original Yes result contract");
  }
  static void MemoryRoundTrip(string root) {
   var clock=new FakeClock();
   bool enabled=true;
   string path=Path.Combine(root,"memories.json");
   var repository=new JsonRepository<MemoryBook>(path);
   var memory=new MemoryService(delegate {return enabled;},repository,clock);
   int changes=0;memory.Changed+=delegate {changes++;};
   Check(memory.Remember(new[]{new MemoryItem{Text="只喝冰美式，不加糖",Kind="偏好",Pending=true,Source="推断"}})==1,"new memory persisted");
   clock.Value=clock.Value.AddDays(1);
   Check(memory.Remember(new[]{new MemoryItem{Text="只喝冰美式，不加糖",Kind="偏好",Importance=0.9,Core=true,Due=clock.UtcNow}})==0,"merge preserves added-count contract");
   var reload=new MemoryService(delegate {return true;},new JsonRepository<MemoryBook>(path),clock);
   MemoryItem item=reload.Items[0];
   Check(item.Hits==2&&item.LastSeen==clock.UtcNow&&item.Importance==0.9&&item.Core&&!item.Pending&&item.Due==clock.UtcNow,"merged values survive service restart");
   Check(changes==2&&memory.Revision==2,"merge notifies open panels");
   enabled=false;
   string before=File.ReadAllText(path);
   string reply="收到。[[REMEMBER]]kind=事实|text=这是关闭后迟到的记忆[[/REMEMBER]]";
   memory.Observe(ref reply);
   Check(memory.Count==1&&changes==2&&File.ReadAllText(path)==before,"late memory marker cannot write while disabled");
   Check(!MemoryMarkers.HasMarker(reply),"disabled memory markers stay out of chat UI");
   enabled=true;memory.Forget(item.Id);
   int revision=memory.Revision;
   memory.Remember(new[]{new MemoryItem{Text="只喝冰美式，不加糖",Kind="偏好"}});
   Check(memory.Count==0&&memory.Revision==revision,"forgotten memory cannot return through merge");
   string blocker=Path.Combine(root,"blocked");File.WriteAllText(blocker,"file");
   var failed=new MemoryService(delegate{return true;},new JsonRepository<MemoryBook>(Path.Combine(blocker,"memory.json")),clock);
   failed.Remember(new[]{new MemoryItem{Text="需要保存的记忆"}});
   Check(failed.LastError.Length>0,"failed memory save is visible");
   File.Delete(blocker);failed.Save();
   Check(failed.LastError.Length==0,"successful retry clears stale storage error");
  }
  static void ConversationInputs(string root) {
   var state=new PetState();var clock=new FakeClock();
   var session=new ChatSession(new TransientRepository<ChatStore>(new ChatStore()));
   string evidence=new string('资',7000)+"最后来源 https://example.test/last";
   var request=new ConversationRequest(state,"现在有什么消息","","","","记忆可用。你记得的事：\n- 只喝冰美式",evidence,clock.Now);
   ChatTurn[] first=session.Build(request);
   Check(first[first.Length-1].Content.Contains("example.test/last")&&first[first.Length-1].Content.Contains("不要编造资料"),"web evidence tail and source instructions survive context assembly");
   Check(first[0].Content.StartsWith(CharacterPersona.Prompt,StringComparison.Ordinal)&&first[first.Length-1].Content.EndsWith("现在有什么消息",StringComparison.Ordinal),"web answer keeps shared persona and returns to the original question after evidence");
   ChatTurn[] next=session.Build(new ConversationRequest(state,"一个新话题","","","","","",clock.Now));
   Check(!next[0].Content.Contains("冰美式"),"next turn never inherits stale recalled memory");
   Check(first[0].Content.Contains("冰美式"),"explicit memory is included in the correct turn");
   var captured=new ConversationRequest(state,"hello","","","","","",clock.Now);
   string prompt=captured.SystemPrompt;state.Sleeping=!state.Sleeping;
   Check(captured.SystemPrompt==prompt,"request prompt is stable after settings change");
   int tokens;
   var history=new[]{new ChatTurn("user",new string('旧',1000)),new ChatTurn("assistant","最近的一轮")};
   int budget=ConversationContext.Estimate(new ChatTurn("system",captured.SystemPrompt))+ConversationContext.Estimate(new ChatTurn("user",captured.UserPrompt))+30;
   var limited=ConversationContext.Build(history,captured,budget,out tokens);
   Check(limited.Length==3&&limited[1].Content=="最近的一轮"&&tokens<=budget,"context budget retains most recent history");
   var disk=new ChatSession(new JsonRepository<ChatStore>(Path.Combine(root,"chat.json")));
   disk.Remember("用户原话","回答");
   var reopened=new ChatSession(new JsonRepository<ChatStore>(Path.Combine(root,"chat.json")));reopened.Load();
   Check(reopened.History.Length==2&&reopened.History[0].Content=="用户原话","chat history format remains compatible");
  }
  static void ChatCopyAffordance() {
   var view=new ChatView(false);
   view.Add(new ChatLine(ChatSpeaker.Elyms,"可以选中的回复",""));
   var stack=(StackPanel)typeof(ChatView).GetField("stack",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(view);
   Check(stack.Children.Count==1,"chat view contains one message block");
   var block=(StackPanel)stack.Children[0];
   var bubble=(Border)block.Children[1];
   var text=bubble.Child as TextBox;
   Check(text!=null&&text.IsReadOnly&&text.Text=="可以选中的回复","chat message uses selectable read-only text");
   Check(text.ContextMenu!=null&&text.ContextMenu.Items.Count>=3,"chat message exposes copy menu");
   var input=ChatInput.Field();
   Check(input.ContextMenu!=null&&input.ContextMenu.Items.Count>=4,"chat input exposes copy menu");
  }
  static void PersonaIntegration() {
   string baseline=CharacterPersona.Prompt;
   var state=new PetState();DateTime now=new DateTime(2026,11,9,12,0,0);
   Check(baseline.Length>1000&&baseline.Contains("不是小 Y 长大的样子"),"shipped persona resource loads with identity boundary");
   string offline=Persona.SystemPrompt(state,now,"","DSH 不可用","联网不可用","记忆关闭了。");
   Check(offline.StartsWith(baseline,StringComparison.Ordinal)&&!offline.Contains("[[DSH_TASK]]")&&!offline.Contains("[[WEB_SEARCH]]")&&!offline.Contains("[[REMEMBER]]"),"shared persona does not enable disabled capabilities");
   string online=Persona.SystemPrompt(state,now,"","DSH 工作台在线","联网可用","记忆可用");
   Check(online.StartsWith(baseline,StringComparison.Ordinal)&&online.Contains("[[DSH_TASK]]")&&online.Contains("[[WEB_SEARCH]]")&&online.Contains("[[REMEMBER]]"),"runtime capability instructions survive persona composition");
   Check(ArchiveInterview.BuildSystem(null).StartsWith(baseline,StringComparison.Ordinal)&&ArchiveInterview.BuildSystem(null).Contains(ArchiveInterview.SchemaHint),"structured interview shares persona while preserving JSON contract");
   Check(ArchiveInterview.BuildQuestion(null)[0].Content.StartsWith(baseline,StringComparison.Ordinal),"interview question shares character baseline");
   Check(Dialogue.Reply("生日快乐").Contains("入职")&&Dialogue.Reply("我忙，别打扰").IndexOf("？",StringComparison.Ordinal)<0,"offline birthday and refusal respect character adaptation");
   Check(baseline.Contains("灵可")&&baseline.Contains("卡厄斯")&&baseline.Contains("贞"),"persona names the squad");
   Check(Dialogue.Reply("你不就是长大的 Y 吗").Contains("两件事"),"offline identity follows the persona example");
   Check(Dialogue.Reply("type you").IndexOf("两件事",StringComparison.Ordinal)<0,"standalone y detection ignores english words");
  }
  static void MarkerTolerance() {
   string web="[[WEB_SEARCH]]上海 天气[/WEB_SEARCH]]";
   WebIntent intent=WebTask.Extract(ref web);
   Check(intent.Present&&intent.Value=="上海 天气"&&web.Length==0,"malformed web close tag is tolerated");
   string dsh="好的。\n[[DSH_TASK]]写一个 README[/DSH_TASK]]";
   string task=DshTask.Extract(ref dsh);
   Check(task=="写一个 README"&&dsh=="好的。","malformed dsh close tag is tolerated");
   string memory="记下了。[[REMEMBER]]kind=偏好;text=冰美式[/REMEMBER]]";
   MemoryItem[] items=MemoryMarkers.Extract(ref memory);
   Check(items.Length==1&&items[0].Text=="冰美式"&&memory=="记下了。","malformed memory close tag is tolerated");
  }
  static void ModelResponseMetadata() {
   const string prefix="{\"model\":\"deepseek-flash\",\"choices\":[{\"message\":{\"content\":\"";
   const string suffix="\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":20}}";
   var complete=DeepSeek.ParseResponse(prefix+"完整回复"+suffix,1);
   Check(complete.Ok&&!complete.Truncated&&complete.Model=="deepseek-flash"&&complete.FinishReason=="stop","evaluation records provider model and completion status");
   var limited=DeepSeek.ParseResponse((prefix+"未完的内容"+suffix).Replace("\"stop\"","\"length\""),1);
   Check(limited.Ok&&limited.Truncated&&limited.Text=="未完的内容"&&limited.CompletionTokens==20,"nonempty token-limited reply is flagged without losing content or usage");
   var clipped=DeepSeek.ParseResponse(prefix+new string('长',4001)+suffix,1);
   Check(clipped.Ok&&clipped.Truncated&&clipped.FinishReason=="stop","local display clipping is not mistaken for a complete model answer");
  }
  static void WorkLifecycle() {
   var queue=new ManualScheduler();int results=0,failures=0;string last="";
   var scope=new WorkScope(queue,delegate(string error){failures++;last=error;},true);
   scope.Run<int>(delegate {throw new IOException("secret-key");},delegate(int value){results++;});queue.Drain();
   Check(failures==1&&results==0&&!last.Contains("secret-key"),"background exception returns safe failure on completion queue");
   scope.Run(delegate{return 7;},delegate(int value){results+=value;});queue.Drain();
   Check(results==7,"scope remains usable after a failure");
   scope.Run(delegate{return 8;},delegate(int value){results+=value;});queue.RunWork();scope.CancelPending();queue.RunCompletions();
   Check(results==7,"cancel invalidates already queued completion");
   scope.Run(delegate{results+=100;return 9;},delegate(int value){results+=value;});scope.Dispose();queue.Drain();
   Check(results==7,"dispose prevents queued work from starting");
   var preview=new WorkScope(queue,delegate(string error){failures++;},false);
   preview.Run<int>(delegate {throw new Exception("must not execute");},delegate(int value){results++;});
   Check(failures==2&&queue.Work.Count==0,"preview refuses external work before queuing it");
  }
  static void AutonomyEligibility() {
   var state=new PetState();
   Check(AutonomyPolicy.CanExecute(state,false,false,false,false),"idle enabled autonomy may execute");
   state.AutoLife=false;Check(!AutonomyPolicy.CanExecute(state,false,false,false,false),"disabled during planning blocks action");
   state.AutoLife=true;state.Sleeping=true;Check(!AutonomyPolicy.CanExecute(state,false,false,false,false),"sleep during planning blocks action");
   state.Sleeping=false;state.PausedSeconds=60;Check(!AutonomyPolicy.CanExecute(state,false,false,false,false),"paused focus blocks action");
   state.PausedSeconds=0;state.FocusEnd=DateTime.UtcNow.AddMinutes(1);Check(!AutonomyPolicy.CanExecute(state,false,false,false,false),"active focus blocks action");
   state.FocusEnd=DateTime.MinValue;
   Check(!AutonomyPolicy.CanExecute(state,true,false,false,false)&&!AutonomyPolicy.CanExecute(state,false,true,false,false)&&!AutonomyPolicy.CanExecute(state,false,false,true,false),"user activity wins over autonomous plan");
   Check(!AutonomyPolicy.CanExecute(state,false,false,false,true),"disposed autonomy cannot commit a plan");
  }
  static void PreviewIsolation(string root) {
   string folder=Path.Combine(root,"preview");Directory.CreateDirectory(folder);
   string history=Path.Combine(folder,"chat-history.json");File.WriteAllText(history,"private sentinel");
   var feedback=new PetFeedback(delegate {},delegate {},delegate {});
   using(var services=new PetServices(new PetState(),new ManualScheduler(),feedback,delegate{return false;},folder,true,new FakeClock())) {
    Check(services.Conversation.Lines.Length==0,"preview does not load personal history");
    services.Conversation.AddAutonomous("预览内容","预览");services.Memory.Remember(new[]{new MemoryItem{Text="预览记忆"}});services.Archive.Save();
    Check(File.ReadAllText(history)=="private sentinel"&&Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Length==1,"preview does not write personal data");
   }
   Storage.Folder=folder;
   var pet=new PetWindow(new PetState(),true);
   try {
    int notifications=0;
    var field=typeof(MemoryService).GetField("Changed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    var panel=new MemoryPanel(pet);
    var listeners=(Delegate)field.GetValue(pet.Memory);notifications=listeners==null?0:listeners.GetInvocationList().Length;
    panel.Dispose();listeners=(Delegate)field.GetValue(pet.Memory);
    Check(notifications==1&&(listeners==null||listeners.GetInvocationList().Length==0),"memory panel releases service subscription");
   } finally {pet.Close();}
  }
  static void AutonomyLateCompletion(string root) {
   string original=Storage.Folder;
   Storage.Folder=Path.Combine(root,"autonomy");
   var queue=new ManualScheduler();var state=new PetState {AiChat=false};
   var feedback=new PetFeedback(delegate {},delegate {},delegate {});
   var services=new PetServices(state,queue,feedback,delegate{return false;},Storage.Folder,false,new FakeClock(),
    delegate(AutonomyRequest request) {return new AutonomyPlan {Type="chat",Message="迟到的自主消息"};});
   try {
    services.Autonomy.RunNow();
    Check(services.Autonomy.Running&&queue.Work.Count==1,"autonomy planning starts asynchronously");
    state.Sleeping=true;queue.Drain();
    Check(!services.Autonomy.Running&&services.Autonomy.LastType=="skipped"&&services.Conversation.Lines.Length==0,"late plan is rejected by actual service after sleep");
    state.Sleeping=false;services.Autonomy.RunNow();queue.RunWork();services.Dispose();queue.RunCompletions();
    Check(!services.Autonomy.Running&&services.Conversation.Lines.Length==0,"disposed service drops completed autonomous plan");
   } finally {services.Dispose();Storage.Folder=original;}
  }
  static void AutonomySchedule(string root) {
   string original=Storage.Folder;
   Storage.Folder=Path.Combine(root,"autonomy-schedule");
   var clock=new FakeClock();
   var state=new PetState{AiChat=false};
   var queue=new ManualScheduler();
   var feedback=new PetFeedback(delegate {},delegate {},delegate {});
   var services=new PetServices(state,queue,feedback,delegate{return false;},Storage.Folder,false,clock,
    delegate(AutonomyRequest request) {return new AutonomyPlan {Type="rest"};});
   try {
    var field=typeof(AutonomyService).GetField("nextAt",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    var tick=typeof(AutonomyService).GetMethod("Tick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
    DateTime target=clock.Now.AddMinutes(30);
    field.SetValue(services.Autonomy,target);
    state.Sleeping=true;
    tick.Invoke(services.Autonomy,null);
    Check(services.Autonomy.NextAt==target,"sleep before the due time does not pull autonomy earlier");
    field.SetValue(services.Autonomy,clock.Now.AddMinutes(-1));
    tick.Invoke(services.Autonomy,null);
    Check(services.Autonomy.NextAt>clock.Now.AddMinutes(9)&&services.Autonomy.LastSummary.Contains("休息"),"sleep at the due time postpones by ten minutes");
   } finally {
    services.Dispose();
    Storage.Folder=original;
   }
  }
  static void AiGating() {
   var state=new PetState{AiChat=false};
   Check(!AutonomyPlanner.CanUseAi(state,"sk-test-12345678"),"online AI off blocks autonomous planner");
   Check(!AutonomyPlanner.CanUseAi(state,""),"missing key blocks autonomous planner");
   state.AiChat=true;
   Check(AutonomyPlanner.CanUseAi(state,"sk-test-12345678"),"online AI on allows autonomous planner");
  }
  static void StatusUpdates() {
   var state=new PetState();var model=new CompanionStatusViewModel();var now=DateTime.UtcNow;
   int events=0;model.PropertyChanged+=delegate {events++;};
   model.Refresh(state,now);int first=events;model.Refresh(state,now);
   Check(events==first,"unchanged status does not notify WPF again");
   state.StartFocus(1,"专注",now);model.Refresh(state,now.AddSeconds(5));
   Check(model.FocusClock=="00:55"&&model.CanPause,"focus view model shows remaining duration");
   state.TogglePause(now.AddSeconds(10));model.Refresh(state,now.AddMinutes(3));
   Check(model.FocusClock=="00:50"&&model.PauseLabel=="继续计时","paused timer display stays stable");
  }
  static void ConcurrentPersistence(string root) {
   string path=Path.Combine(root,"concurrent.json");
   Parallel.For(0,30,delegate(int index) {
    var repository=new JsonRepository<MemoryBook>(path);
    if(!repository.Save(new MemoryBook{Schema=index+1,Updated=DateTime.UtcNow}))throw new IOException(repository.LastError);
   });
   var saved=new JsonRepository<MemoryBook>(path).Load();
   Check(saved!=null&&saved.Schema>=1&&saved.Schema<=30,"concurrent atomic writes leave valid JSON");
   Check(Directory.GetFiles(root,"*.tmp").Length==0,"atomic writes leave no temporary files");
   File.WriteAllText(path,"corrupt");
   Check(new JsonRepository<MemoryBook>(path).Load()!=null,"corrupted main file recovers from backup");
  }
  sealed class FakeClock : IClock {
   public DateTime Value=new DateTime(2026,11,9,12,0,0,DateTimeKind.Utc);
   public DateTime Now {get{return Value.ToLocalTime();}}
   public DateTime UtcNow {get{return Value;}}
  }
  sealed class ManualScheduler : IWorkScheduler {
   public readonly Queue<Action> Work=new Queue<Action>();
   readonly Queue<Action> completions=new Queue<Action>();
   public int Executions;
   public void Queue(Action action){Work.Enqueue(action);}
   public void Post(Action action){completions.Enqueue(action);}
   public void RunWork(){while(Work.Count>0){Executions++;Work.Dequeue()();}}
   public void RunCompletions(){while(completions.Count>0)completions.Dequeue()();}
   public void Drain(){RunWork();RunCompletions();}
  }
 }
}
