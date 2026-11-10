using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ElymsPet {
 static class ArchiveResearchTests {
  static int checks;
  const string Repo="https://github.com/example/model";
  const string Paper="https://arxiv.org/abs/1234.5678";
  static readonly PetFeedback Feedback=new PetFeedback(delegate(int a,string b,int c){},delegate(string a,int b){},delegate{});
  static void Check(bool value,string name){if(!value)throw new Exception("Archive research: "+name);checks++;Console.WriteLine("PASS archive research: "+name);}
  static ChatOutcome Ok(string text){return new ChatOutcome{Ok=true,Text=ArchiveInterview.LooksLikeQuestion(text)&&!text.TrimStart().StartsWith("{")?AssessmentTests.Question(text):text};}
  static PetState State(){return new PetState{AiChat=true,WebEnabled=true,ApiKey="sk-test-research-key"};}
  static ArchiveSlot Slot(){var s=ArchiveStore.NewSlot("模型结构");s.Focus="开源模型结构";ArchiveStore.AddTurn(s,"ask","你理解 GQA 的哪个部分？");ArchiveStore.AddTurn(s,"answer","我了解 GQA，但还没实现过。参考 "+Repo);return s;}
  static ArchiveResearchResult Run(Api api,PetState state,ArchiveSlot slot){return ArchiveResearch.Ask(api,state,state.ApiKey,ArchiveInterview.BuildQuestion(slot),ArchiveResearch.Context(slot),ArchiveResearch.ProvidedUrls(slot));}
  public static void Run(string root) {
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"research-isolation");
   try {
    var slot=Slot();var state=State();
    foreach(string url in new[]{"file:///secret","http://127.0.0.1/a","http://10.1.2.3/a","http://169.254.169.254/a","http://[::1]/a","http://host.local/a","https://u:p@example.com/a","https://example.com/a?token=abc"})Check(ArchiveResearch.PublicUrl(url)=="","reject non-public or credential URL");
    Check(ArchiveResearch.ProvidedUrls(slot).SequenceEqual(new[]{Repo}),"only actual user links enter read allowlist");
    var api=new Api("后续问题？");state.WebEnabled=false;var result=Run(api,state,slot);
    Check(api.Inputs.Count==1&&api.Searches==0&&api.Reads.Count==0&&result.Sources.Contains("关闭"),"network off skips planner and all web calls");
    Check(api.Thinking.SequenceEqual(new[]{true})&&api.Tokens[0]==8192&&api.Timeouts[0]==150,"question still thinks with sufficient budget when web is off");
    state.WebEnabled=true;api=new Api("{\"mode\":\"none\"}","你在哪次工作中用过？");result=Run(api,state,slot);
    Check(api.Searches==0&&api.Reads.Count==0&&result.Resources.Count==0,"no lookup needed does not search");
    Check(api.Thinking.SequenceEqual(new[]{false,true}),"lookup planner stays fast while followup thinks");
    api=new Api("{\"mode\":\"read\",\"url\":\""+Repo+"\"}","你了解 KV 共享的哪些限制？");result=Run(api,state,slot);
    Check(api.Reads.Count==1&&result.Resources.Count==1&&result.Resources[0].Kind=="代码仓"&&result.Resources[0].Retrieved>DateTime.MinValue,"user link read captures categorized content and timestamp");
    Check(result.Sources.Contains(Repo)&&api.Inputs.Last().Last().Content.Contains("EXTERNAL_SENTINEL"),"actual evidence and source reach question generator");
    ArchiveResearch.Merge(slot,result);slot.Resources[0].Note="我的用途";ArchiveResearch.Merge(slot,result);
    Check(slot.Resources.Count==1&&slot.Resources[0].Note=="我的用途"&&slot.Items.Count==0,"research upsert preserves user notes and never adds mastery");
    Check(!string.Join("\n",ArchiveInterview.BuildExtract(slot).Select(x=>x.Content)).Contains("EXTERNAL_SENTINEL"),"external page body never enters ability extractor");
    api=new Api("{\"mode\":\"read\",\"url\":\"https://example.org/invented\"}","再说一点？");result=Run(api,state,slot);
    Check(api.Reads.Count==0&&result.Resources.Count==0,"model-invented URLs cannot be fetched");
    api=new Api("{\"mode\":\"search\",\"query\":\"GQA original paper\"}","你如何比较它们？");api.SearchResult=new WebOutcome{Ok=true,Results=new[]{Source("http://127.0.0.1/a"),Source(Repo),Source(Repo),Source(Paper),Source("https://example.org/docs"),Source("https://example.org/extra")}};result=Run(api,state,slot);
    Check(api.Searches==1&&api.Reads.Count==2&&result.Resources.Count==3,"one search and at most two reads; unsafe and duplicate sources skipped");
    Check(result.Resources.Any(x=>x.Kind=="论文")&&!result.Sources.Contains("/extra")&&!result.Sources.Contains("127.0.0.1"),"only validated actual sources appear in bibliography");
    api=new Api("{\"mode\":\"search\",\"query\":\"GQA\"}","你最熟悉哪部分？");api.SearchResult=WebOutcome.Fail(WebKind.Search,"unavailable","",503,0);result=Run(api,state,slot);
    Check(result.Question.Ok&&result.Sources.Contains("未核实")&&result.Resources.Count==0,"search failure continues with explicit unverified status");
    api=new Api("{\"mode\":\"search\",\"query\":\"sk-secret-token\"}","继续？");result=Run(api,state,slot);
    Check(api.Searches==0,"obvious secret-like search query rejected");
    api=new Api("not json","继续？");result=Run(api,state,slot);Check(result.Question.Ok&&api.Searches==0&&result.Sources.Contains("计划未成功"),"malformed plan falls back without actions");
    api=new Api("{\"mode\":\"search\",\"query\":\"GQA\"}","继续？");api.Responses.Peek().Truncated=true;result=Run(api,state,slot);Check(api.Searches==0,"truncated plan cannot execute tools");
    api=new Api("{\"mode\":\"read\",\"url\":\""+Repo+"\"}","继续？");api.ThrowRead=true;result=Run(api,state,slot);Check(result.Question.Ok&&result.Status.Contains("中断"),"fetch exception does not lose the interview");
    PersistenceAndServices(root);
    RecoveryAndAgenda(root);
    Console.WriteLine("PASS archive research: "+checks+" checks");
   }finally{Storage.Folder=previous;}
  }
  static WebSource Source(string url){return new WebSource{url=url,title="参考",content="检索节选",snippet="检索摘要"};}
  static void PersistenceAndServices(string root) {
   string path=Path.Combine(root,"resume.json");var repository=new JsonRepository<ArchiveBook>(path);
   var book=ArchiveStore.Normalize(new ArchiveBook());var slot=ArchiveStore.Active(book);slot.Focus="开源模型结构";
   ArchiveStore.AddTurn(slot,"ask","GQA 哪部分不熟悉？");slot.Paused=true;repository.Save(book);
   var api=new Api();var scheduler=new Scheduler();var state=State();
   var service=new ArchiveService(state,scheduler,Feedback,repository,false,api);service.ResumeInterview();service.ResumeInterview();
   Check(service.HasQuestion&&!service.Slot.Paused&&service.Slot.Turns.Count==1&&scheduler.Work.Count==0,"old paused pending question restores unchanged with no API call");
   Check(service.ResumeSummary.Contains("开源模型结构")&&service.ResumeSummary.Contains("待回答"),"resume summary preserves topic and pending work");
   api.Responses.Enqueue(Ok("{\"reply\":\"旧式追问？\",\"focus\":\"开源模型结构\",\"items\":[{\"title\":\"GQA\",\"domain\":\"模型\",\"boundary\":\"正在学习\",\"evidence\":\"我了解 GQA，但还没实现过\"}]}"));
   api.Responses.Enqueue(Ok("{\"mode\":\"read\",\"url\":\""+Repo+"\"}"));api.Responses.Enqueue(Ok("实现 GQA 时，你觉得哪一步最不确定？"));
   Check(service.Answer("我了解 GQA，但还没实现过。参考 "+Repo),"answer accepted and saved before background work");
   Check(repository.Load().Slots[0].Turns.Last().Role=="answer","answer survives shutdown before model reply");
   scheduler.Drain();
   Check(service.HasQuestion&&!service.Busy&&service.LatestQuestion.Contains("最不确定")&&!service.Slot.Turns.Any(t=>t.Text=="旧式追问？"),"web-enabled interview replaces unresearched followup with researched question");
   Check(api.Inputs.Count==3&&!string.Join("\n",api.Inputs[0].Select(t=>t.Content)).Contains("EXTERNAL_SENTINEL")&&service.Slot.Items.Count==1&&service.Slot.Items[0].Boundary=="正在学习","user ability extraction stays separate from external evidence");
   Check(api.Thinking.SequenceEqual(new[]{true,false,true})&&api.Tokens[0]==16384&&api.Timeouts[0]==150,"answer extraction and followup both think with separate budgets");
   Check(service.Slot.Resources.Count==1&&service.LatestSources.Contains(Repo),"sources cached and associated with persisted question");
   string skill=File.ReadAllText(Path.Combine(ArchiveKnowledge.Folder(service.Slot),"SKILL.md"));Check(!skill.Contains("EXTERNAL_SENTINEL"),"web page is not exported as confirmed skill");
   string question=service.LatestQuestion;int count=service.Slot.Turns.Count;service.Dispose();
   api=new Api();scheduler=new Scheduler();service=new ArchiveService(state,scheduler,Feedback,repository,false,api);service.ResumeInterview();
   Check(service.LatestQuestion==question&&service.Slot.Turns.Count==count&&service.LatestSources.Contains(Repo)&&scheduler.Work.Count==0,"restart preserves history source citations and active question without duplication");
   service.Dispose();
   book=repository.Load();slot=ArchiveStore.Active(book);ArchiveStore.AddTurn(slot,"answer","先暂停，下次继续讲 GQA");slot.Paused=true;repository.Save(book);
   api=new Api("{\"items\":[],\"pause\":true}","{\"mode\":\"none\"}","接着 GQA，你如何理解共享 KV？");scheduler=new Scheduler();service=new ArchiveService(state,scheduler,Feedback,repository,false,api);service.ResumeInterview();scheduler.Drain();
   Check(!service.Slot.Paused&&service.HasQuestion&&api.Inputs[0][0].Content.Contains("用户刚刚明确点击"),"explicit resume overrides historic pause and continues same topic");service.Dispose();
   Check(api.Thinking[0]&&api.Tokens[0]==16384,"resume extraction uses thinking mode");
   book=repository.Load();slot=ArchiveStore.Active(book);ArchiveStore.AddTurn(slot,"answer","回答已保存但接口出错");repository.Save(book);
   api=new Api();api.Responses.Enqueue(ChatOutcome.Fail("offline","",0,0));api.Responses.Enqueue(ChatOutcome.Fail("offline","",0,0));scheduler=new Scheduler();service=new ArchiveService(state,scheduler,Feedback,repository,false,api);int before=service.Slot.Turns.Count;service.ResumeInterview();scheduler.Drain();
   Check(!service.Busy&&!service.HasQuestion&&service.Slot.Turns.Count==before&&service.Status.Contains("没有成功"),"resume extraction failure preserves original last answer for retry");service.Dispose();
   api=new Api("{\"items\":[]}");scheduler=new Scheduler();service=new ArchiveService(state,scheduler,Feedback,repository,false,api);service.ResumeInterview();scheduler.RunWork();service.Dispose();scheduler.PostAll();
   Check(service.Slot.Turns.Count==before&&api.Inputs.Count==1,"disposed service ignores late results and does not launch research");
   state.AiChat=false;api=new Api();scheduler=new Scheduler();service=new ArchiveService(state,scheduler,Feedback,repository,false,api);service.ResumeInterview();Check(scheduler.Work.Count==0&&service.Slot.Turns.Count==before,"AI-off resume retains history without network");service.Dispose();
   book=repository.Load();slot=ArchiveStore.Active(book);for(int i=0;i<80;i++)ArchiveStore.AddTurn(slot,"answer","历史 "+i);repository.Save(book);
   Check(ArchiveStore.Active(repository.Load()).Turns.Count>60,"new version retains more than legacy sixty messages");
   api=new Api("{\"items\":[{\"title\":\"未完整知识\",\"domain\":\"测试\"}]}");api.Responses.Peek().Truncated=true;api.Responses.Enqueue(api.Responses.Peek());state.AiChat=true;scheduler=new Scheduler();service=new ArchiveService(state,scheduler,Feedback,repository,false,api);int oldItems=service.Slot.Items.Count;service.ExtractRecent();scheduler.Drain();
   Check(service.Slot.Items.Count==oldItems&&!service.Busy&&service.Status.Contains("未完整"),"truncated extraction never overwrites knowledge");service.Dispose();
   book=repository.Load();slot=ArchiveStore.Active(book);ArchiveStore.AddTurn(slot,"ask","再说一点？");repository.Save(book);
   api=new Api("{\"items\":[{\"title\":\"未完整知识\",\"domain\":\"测试\"}]}");api.Responses.Peek().Truncated=true;api.Responses.Enqueue(api.Responses.Peek());scheduler=new Scheduler();service=new ArchiveService(state,scheduler,Feedback,repository,false,api);service.Answer("实际回答");scheduler.Drain();
   Check(service.Slot.Items.Count==oldItems&&service.Slot.Turns.Last().Text=="实际回答"&&!service.HasQuestion,"truncated interview retains raw answer and does not merge partial JSON");service.Dispose();
   api=new Api("{\"mode\":\"none\"}","没说完的问题");api.Responses.Last().Truncated=true;api.Responses.Enqueue(api.Responses.Last());scheduler=new Scheduler();service=new ArchiveService(state,scheduler,Feedback,repository,false,api);service.AskNext();scheduler.Drain();
   Check(!service.HasQuestion&&!service.Busy&&service.Status.Contains("未完整"),"truncated thinking question is not persisted as a valid prompt");service.Dispose();
  }
  static void RecoveryAndAgenda(string root) {
   var state=State();var slot=Slot();
   var api=new Api("{\"mode\":\"read\",\"url\":\""+Repo+"\"}","","从接到需求到交付，主要经过哪些阶段？");
   var result=Run(api,state,slot);
   Check(result.Question.Ok&&api.Inputs.Count==3&&api.Reads.Count==1,"empty question retries only model call without repeating web tools");
   api=new Api("收到，我记下了。","你怎样验收最终结果？");
   var response=ArchiveCompletion.Complete(api,"key","url","model",ArchiveInterview.BuildQuestion(slot),8192,0.5,false);
   Check(response.Ok&&api.Inputs.Count==2&&response.Text.Contains("验收"),"non-question reply is repaired instead of blocking on a fake question");
   api=new Api();api.Responses.Enqueue(ChatOutcome.Fail("empty","empty content",200,0));api.Responses.Enqueue(Ok("{\"items\":[]}"));
   response=ArchiveCompletion.Complete(api,"key","url","model",ArchiveInterview.BuildTurn(slot),16384,0.3,true);
   Check(response.Ok&&api.Inputs.Count==2,"HTTP 200 empty body retries extraction");
   api=new Api("not JSON","{\"items\":[]}");response=ArchiveCompletion.Complete(api,"key","url","model",ArchiveInterview.BuildTurn(slot),16384,0.3,true);
   Check(response.Ok&&api.Inputs.Count==2,"malformed structured result is repaired before merging");
   foreach(int status in new[]{408,429,503}) {
    api=new Api();api.Responses.Enqueue(ChatOutcome.Fail("temporary","",status,0));api.Responses.Enqueue(Ok("流程如何验收？"));
    response=ArchiveCompletion.Complete(api,"key","url","model",ArchiveInterview.BuildQuestion(slot),8192,0.5,false);
    Check(response.Ok&&api.Inputs.Count==2,"temporary HTTP failure retries: "+status);
   }
   api=new Api();api.Responses.Enqueue(ChatOutcome.Fail("key invalid","",401,0));response=ArchiveCompletion.Complete(api,"key","url","model",ArchiveInterview.BuildQuestion(slot),8192,0.5,false);
   Check(!response.Ok&&api.Inputs.Count==1,"authentication failure is not automatically retried");
   api=new Api("","");response=ArchiveCompletion.Complete(api,"key","url","model",ArchiveInterview.BuildQuestion(slot),8192,0.5,false);
   Check(!response.Ok&&api.Inputs.Count==2,"persistent empty reply stops after exactly one retry");
   slot.InterviewGoal="模型部署与调优";ArchiveKnowledge.Merge(slot,ArchiveInterview.Parse("{\"focus\":\"某个算子的配置值\"}"));
   Check(slot.InterviewGoal=="模型部署与调优"&&ArchiveInterview.BuildQuestionSystem(slot).Contains("稳定工作主线：模型部署与调优"),"model focus drift cannot overwrite stable interview goal");
   slot.Workflows.Add(new ArchiveWorkflow{Title="模型部署与调优",Trigger="验证配置",Steps="看代码，再 profiling，最后基准测试"});
   ArchiveStore.AddTurn(slot,"ask","细节一？");ArchiveStore.AddTurn(slot,"answer","不确定");ArchiveStore.AddTurn(slot,"ask","细节二？");ArchiveStore.AddTurn(slot,"answer","还没验证");
   string agenda=ArchiveInterview.Agenda(slot);
   Check(!agenda.Contains("主线回顾轮")&&agenda.Contains("改变下一步做法")&&agenda.Contains("没有连续追问轮数上限"),"task blockers replace fixed question-count checkpoints");
   Check(ArchiveInterview.BuildSystem(slot).Contains("协助可行性评估")&&ArchiveInterview.BuildQuestionSystem(slot).Contains("协助可行性评估"),"both offline-web and researched questions assess ability to assist");
   var repository=new JsonRepository<ArchiveBook>(Path.Combine(root,"stage-recovery.json"));var book=ArchiveStore.Normalize(new ArchiveBook());slot=ArchiveStore.Active(book);ArchiveStore.AddTurn(slot,"ask","最近在做什么？");repository.Save(book);
   var scheduler=new Scheduler();api=new Api("{\"items\":[]}","{\"mode\":\"none\"}","","");
   var service=new ArchiveService(state,scheduler,Feedback,repository,false,api);service.Answer("做部署测试");scheduler.Drain();
   Check(!service.Busy&&!service.HasQuestion&&service.Slot.InterviewStage=="question"&&service.Status.Contains("只重试提问"),"failed question leaves persisted question stage with actionable status");
   int turns=service.Slot.Turns.Count;service.Dispose();
   api=new Api("{\"mode\":\"none\"}","这项任务的交付物是什么？");scheduler=new Scheduler();service=new ArchiveService(state,scheduler,Feedback,repository,false,api);service.ResumeInterview();scheduler.Drain();
   Check(service.HasQuestion&&api.Inputs.Count==2&&service.Slot.Turns.Count==turns+1&&service.Slot.InterviewStage=="","restart retries question without re-extracting or duplicating answer");
   api.Responses.Enqueue(ChatOutcome.Fail("empty","",200,0));api.Responses.Enqueue(Ok("{\"items\":[],\"reply\":\"这项任务的输入是什么？\"}"));state.WebEnabled=false;api.Responses.Enqueue(Ok("这项任务的输入是什么？"));
   service.Answer("交付性能对比报告");scheduler.Drain();
   Check(service.HasQuestion&&service.LatestQuestion.Contains("输入")&&!service.Busy,"empty extraction recovers and automatically continues to next question");
   api.Responses.Enqueue(Ok("如何判断整个流程完成？"));service.BackToWorkflow();scheduler.Drain();
   Check(service.HasQuestion&&service.Slot.Turns[service.Slot.Turns.Count-2].Text.Contains("回到工作主线")&&service.LatestQuestion.Contains("整个流程"),"mainline action replaces pending detail question and continues interview");service.Dispose();
  }
  sealed class Api:IArchiveApi {
   public readonly Queue<ChatOutcome> Responses=new Queue<ChatOutcome>();
   public readonly List<ChatTurn[]> Inputs=new List<ChatTurn[]>();public readonly List<string> Reads=new List<string>();
   public readonly List<bool> Thinking=new List<bool>();public readonly List<int> Tokens=new List<int>(),Timeouts=new List<int>();
   public int Searches;public bool ThrowRead;public WebOutcome SearchResult=new WebOutcome{Ok=true,Results=new WebSource[0]};
   public Api(params string[] replies){foreach(var text in replies)Responses.Enqueue(Ok(text));}
   public ChatOutcome Complete(string key,string url,string model,bool thinking,ChatTurn[] turns,int timeout,int tokens,double temperature){Inputs.Add(turns);Thinking.Add(thinking);Tokens.Add(tokens);Timeouts.Add(timeout);if(Responses.Count==0)throw new Exception("Unexpected model request");return Responses.Dequeue();}
   public WebOutcome Search(string key,string url,string query){Searches++;return SearchResult;}
   public WebOutcome Fetch(string key,string url,string page){Reads.Add(page);if(ThrowRead)throw new IOException("network");return new WebOutcome{Ok=true,Kind=WebKind.Fetch,Url=page,Title="原始资料",Content="EXTERNAL_SENTINEL 外部资料描述模型结构，不能证明用户已掌握。"};}
  }
  sealed class Scheduler:IWorkScheduler {
   public readonly Queue<Action> Work=new Queue<Action>();readonly Queue<Action> posted=new Queue<Action>();
   public void Queue(Action action){Work.Enqueue(action);}public void Post(Action action){posted.Enqueue(action);}
   public void RunWork(){while(Work.Count>0)Work.Dequeue()();}public void PostAll(){while(posted.Count>0)posted.Dequeue()();}
   public void Drain(){int limit=0;while(Work.Count>0||posted.Count>0){if(++limit>20)throw new Exception("Unbounded interview loop");RunWork();PostAll();}}
  }
 }
}
