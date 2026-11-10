using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace ElymsPet {
 static class AssessmentTests {
  static int checks;
  static void Check(bool ok,string name){if(!ok)throw new Exception("Assessment: "+name);checks++;Console.WriteLine("PASS assessment: "+name);}
  static string Escape(string text){return text.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n");}
  public static string Question(string reply) {return "{\"reply\":\""+Escape(reply)+"\",\"assessment\":{\"task\":\"协助完成当前工作\",\"can_help\":\"可整理已有步骤\",\"missing\":\"关键判据\",\"impact\":\"决定方案是否可验证\",\"next_step\":\"明确判据后给出可核对的方案\",\"mode\":\"ask\"}}";}
  public static void Run(string root) {
   var valid=Question("哪个指标决定是否采用这次改动？");
   var parsed=ArchiveAssessment.Parse(valid);
   Check(parsed!=null&&parsed.Mode=="ask"&&parsed.Display().Contains("关键判据"),"valid assessment describes concrete missing information");
   Check(ArchiveAssessment.Parse(valid.Replace("\"missing\":\"关键判据\"","\"missing\":\"\""))==null,"asking requires a stated information gap");
   Check(ArchiveAssessment.Parse(valid.Replace("\"mode\":\"ask\"","\"mode\":\"execute\""))==null,"assessment cannot request automatic execution");
   Check(ArchiveAssessment.Parse(valid.Replace("\"impact\":\"决定方案是否可验证\",",""))==null,"assessment must explain the practical impact");
   string trial=valid.Replace("\"mode\":\"ask\"","\"mode\":\"try\"").Replace("\"missing\":\"关键判据\"","\"missing\":\"\"");
   Check(ArchiveAssessment.Parse(trial)!=null&&ArchiveAssessment.Parse(trial).Display().Contains("最小试做"),"sufficient information can lead to trial rather than inventing a gap");
   Check(!ArchiveCompletion.Valid(new ChatOutcome{Ok=true,Text="再说一点？"},false),"bare question without assessment must be repaired");
   Check(!ArchiveCompletion.Valid(new ChatOutcome{Ok=true,Text=valid.Replace("哪个指标决定是否采用这次改动？","收到。")},false),"assessment without an actionable user prompt is invalid");
   var slot=ArchiveStore.NewSlot("性能验证");slot.Focus=slot.InterviewGoal="性能验证";
   slot.Items.Add(new ArchiveItem{Title="性能验证的历史约束",Domain="推理部署",Boundary="待核实",Evidence="HISTORICAL_EVIDENCE_42",Note="只有现场能够确定"});
   for(int i=0;i<12;i++){ArchiveStore.AddTurn(slot,"ask","之前的问题 "+i+"？");ArchiveStore.AddTurn(slot,"answer","之前的回答 "+i);}
   string prompt=ArchiveInterview.BuildQuestionSystem(slot);
   Check(prompt.Contains("HISTORICAL_EVIDENCE_42")&&prompt.Contains("待核实"),"assessment receives stored evidence and boundaries outside recent ten turns");
   Check(!prompt.Contains("主线回顾轮")&&!prompt.Contains("最多连续追问两轮")&&prompt.Contains("没有连续追问轮数上限"),"question count cannot interrupt a substantive investigation");
   Check(prompt.Contains("assessment")&&ArchiveInterview.BuildSystem(slot).Contains("assessment"),"researched and combined interview routes use the same assessment contract");
   ArchiveStore.AddTurn(slot,"ask","要先试做并核对吗？");slot.Turns.Last().Assessment=parsed.Display()+" ASSESSMENT_ONLY_SENTINEL";
   Check(!string.Join("\n",ArchiveInterview.BuildExtract(slot).Select(t=>t.Content)).Contains("ASSESSMENT_ONLY_SENTINEL"),"model self-assessment never becomes user knowledge evidence");
   var repo=new JsonRepository<ArchiveBook>(Path.Combine(root,"assessment-persistence.json"));Check(repo.Save(ArchiveStore.Normalize(new ArchiveBook{ActiveId=slot.Id,Slots=new List<ArchiveSlot>{slot}})),"assessment fixture saves successfully");
   var loaded=ArchiveStore.Active(ArchiveStore.Normalize(repo.Load()));
   Check(loaded.Turns.Last().Assessment==slot.Turns.Last().Assessment,"decision summary survives persistence and normalization");
   ArchiveStore.AddTurn(loaded,"answer","回答");loaded.Turns.Last().Assessment="not user evidence";ArchiveStore.Normalize(new ArchiveBook{Slots=new List<ArchiveSlot>{loaded}});
   Check(loaded.Turns.Last().Assessment=="","user turn cannot carry model assessment metadata");
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"assessment-service-isolation");
   try {
    var state=new PetState{AiChat=true,WebEnabled=false,ApiKey="sk-test-assessment"};
    var initial=ArchiveStore.Normalize(new ArchiveBook());ArchiveStore.AddTurn(initial.Slots[0],"ask","你现在要做什么？");repo.Save(initial);
    var api=new Api(valid.Substring(0,valid.Length-1)+",\"items\":[]}");
    var feedback=new PetFeedback(delegate(int a,string b,int c){},delegate(string a,int b){},delegate{});
    var service=new ArchiveService(state,new InlineScheduler(),feedback,repo,false,api);service.Answer("需要判断一次性能改动是否值得合入");
    Check(api.Calls==1&&service.HasQuestion&&service.LatestAssessment.Contains("关键判据"),"combined non-web answer stores valid assessment without extra model call");
    Check(service.Slot.Items.Count==0&&service.Slot.Workflows.Count==0,"self-assessed ability does not create skills or confirmed workflows");
    string saved=service.LatestAssessment;service.Dispose();
    service=new ArchiveService(state,new InlineScheduler(),feedback,repo,false,new Api());service.ResumeInterview();
    Check(service.LatestAssessment==saved&&service.HasQuestion,"resume retains existing question and assessment without new calls");service.Dispose();
   }finally{Storage.Folder=previous;}
   Console.WriteLine("PASS assessment: "+checks+" checks");
  }
  sealed class Api:IArchiveApi {
   readonly Queue<string> replies;public int Calls;
   public Api(params string[] replies){this.replies=new Queue<string>(replies);}
   public ChatOutcome Complete(string key,string url,string model,bool thinking,ChatTurn[] turns,int timeout,int tokens,double temperature){Calls++;return new ChatOutcome{Ok=true,Text=replies.Dequeue(),HasReasoning=true};}
   public WebOutcome Search(string key,string url,string query){throw new Exception("Unexpected web search");}
   public WebOutcome Fetch(string key,string url,string page){throw new Exception("Unexpected web read");}
  }
  sealed class InlineScheduler:IWorkScheduler {public void Queue(Action a){a();}public void Post(Action a){a();}}
 }
}
