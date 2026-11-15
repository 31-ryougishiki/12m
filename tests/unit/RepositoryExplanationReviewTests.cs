using System;
using System.Collections.Generic;
namespace ElymsPet {
 static class RepositoryExplanationReviewTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS explanation review: "+name);}
  static ChatOutcome Ok(string text){return new ChatOutcome{Ok=true,Text=text};}
  static ChatOutcome Questions(string passage,params string[] questions){var result=new RepositoryExplanationReview.ReaderResult{Questions=new List<RepositoryExplanationReview.ReaderQuestion>()};foreach(string q in questions)result.Questions.Add(new RepositoryExplanationReview.ReaderQuestion{Passage=passage,Question=q});return Ok(GraphEditing.Json(result));}
  const string Answers="{\"Answers\":[{\"Id\":1,\"Accepted\":true,\"Answer\":\"它保留在等待队列，下一轮再检查。\",\"Reason\":\"\"}]}";
  public static void Run(){
   var context=new[]{new ChatTurn("system","AUTHOR_SOURCE_SENTINEL"),new ChatTurn("user","讲解当前步骤")};
   var draft=Ok("调度器选择一个候选请求。暂时不能运行的请求会等待。");
   const string body="调度器从 self.waiting 选择一个候选请求。暂时不能运行的请求会等待。";
   var log=new RepositoryExplanationLog();int readers=0,authors=0;
   Func<ChatTurn[],ChatOutcome> reader=turns=>{readers++;bool first=readers==1;Check(turns.Length==2&&turns[0].Content==RepositoryExplanationReview.ReaderRules+(first?RepositoryExplanationReview.ObjectReaderRules:RepositoryExplanationReview.FlowReaderRules)+RepositoryExplanationReview.ReaderScopeRules&&turns[1].Content=="讲解正文：\n"+(first?draft.Text:body),"each reader is isolated and second round sees revised body");return first?Questions("候选请求","等待队列对应哪个变量？"):Questions("会等待","它下一轮还会检查吗？");};
   Func<ChatTurn[],ChatOutcome> author=turns=>{authors++;Check(turns[0].Content.Contains("AUTHOR_SOURCE_SENTINEL"),"both author calls retain evidence");Check(turns[turns.Length-1].Content==(authors==1?RepositoryExplanationReview.RevisionRules:RepositoryExplanationReview.FlowAnswerRules)+RepositoryExplanationReview.EvidenceRules,"author receives distinct round contract");return authors==1?Ok(body):Ok(Answers);};
   var result=RepositoryExplanationReview.Review(draft,context,reader,author,log);
   Check(readers==2&&authors==2&&log.ReviewStatus=="complete"&&log.InitialOutput==draft.Text&&log.ObjectReviewedOutput==body&&log.ObjectQuestions.Count==1&&log.FlowQuestions.Count==1,"two readers and two authors preserve both round records");
   Check(result.Text==body+log.FlowAppendix&&result.Text.StartsWith(body+"\n\n---")&&log.FlowAppendix.Contains("它下一轮还会检查吗？")&&log.FlowAppendix.Contains("下一轮再检查"),"second round appends question and answer without changing body");
   var restored=GraphEditing.Parse<RepositoryExplanationLog>(GraphEditing.Json(log));Check(restored.ObjectReviewedOutput==body&&restored.FlowQuestions.Count==1&&restored.FlowAnswers[0].Accepted&&restored.FlowAppendix==log.FlowAppendix,"two-round logs survive serialization");
   readers=authors=0;log=new RepositoryExplanationLog();result=RepositoryExplanationReview.Review(draft,context,t=>{readers++;return Questions("");},t=>{authors++;return Ok("");},log);
   Check(readers==2&&authors==0&&result==draft&&log.ReviewStatus=="no_questions","empty first round still runs second reader; no empty appendix");
   readers=authors=0;log=new RepositoryExplanationLog();result=RepositoryExplanationReview.Review(draft,context,t=>++readers==1?Questions(""):Questions("会等待","下一轮怎么办？"),t=>{authors++;return Ok(Answers);},log);
   Check(readers==2&&authors==1&&result.Text==draft.Text+log.FlowAppendix,"flow-only questions append to unchanged original body");
   readers=authors=0;log=new RepositoryExplanationLog();result=RepositoryExplanationReview.Review(draft,context,t=>++readers==1?Questions("候选请求","谁？","哪个变量？","属于谁？","作用于谁？"):Questions(""),t=>{authors++;return Ok(body);},log);
   Check(authors==1&&log.ObjectQuestions.Count==4&&result.Text==body,"object audit is not restricted to three questions");
   log=new RepositoryExplanationLog();authors=0;result=RepositoryExplanationReview.Review(draft,context,t=>Questions("not in draft","why"),t=>{authors++;return Ok(body);},log);
   Check(result==draft&&authors==0&&log.ObjectReviewStatus=="failed"&&log.FlowReviewStatus=="skipped","unanchored first round cannot trigger revision or second round");
   readers=authors=0;log=new RepositoryExplanationLog();var longQuote=new string('字',169);result=RepositoryExplanationReview.Review(Ok(longQuote),context,t=>++readers==1?Questions(longQuote,"对应哪个变量？"):Questions(""),t=>{authors++;return Ok(longQuote+"对应 self.waiting。");},log);
   Check(authors==1&&log.ReviewStatus=="complete","intact anchored quote slightly longer than prompt target does not discard whole review");
   readers=authors=0;log=new RepositoryExplanationLog();longQuote=new string('字',513);result=RepositoryExplanationReview.Review(Ok(longQuote),context,t=>Questions(longQuote,"对应哪个变量？"),t=>{authors++;return Ok("正文");},log);
   Check(authors==0&&log.ReviewStatus=="failed","quote tolerance remains bounded");
   foreach(var invalid in new[]{new ChatOutcome{Ok=true,Text="incomplete",Truncated=true},Ok("[[DSH_READ]]more tools"),Ok(new string('x',2000))}){
    log=new RepositoryExplanationLog();result=RepositoryExplanationReview.Review(draft,context,t=>Questions("候选请求","哪个变量？"),t=>invalid,log);
    Check(result==draft&&log.ReviewStatus=="failed","invalid object revision preserves initial explanation");
   }
   foreach(var invalid in new[]{Ok("替换整篇正文"),Ok(Answers.Replace("\"Id\":1","\"Id\":2")),new ChatOutcome{Ok=true,Text=Answers,Truncated=true},Ok(Answers.Replace("下一轮再检查","[[DSH_READ]]"))}){
    readers=authors=0;log=new RepositoryExplanationLog();result=RepositoryExplanationReview.Review(draft,context,t=>++readers==1?Questions("候选请求","哪个变量？"):Questions("会等待","下一轮怎么办？"),t=>++authors==1?Ok(body):invalid,log);
    Check(result.Text==body&&log.FlowReviewStatus=="failed"&&log.ObjectReviewStatus=="complete"&&string.IsNullOrEmpty(log.FlowAppendix),"invalid flow answer preserves successful first-round body");
   }
   readers=0;log=new RepositoryExplanationLog();result=RepositoryExplanationReview.Review(draft,context,t=>++readers==1?Questions(""):Questions("会等待","下一轮怎么办？"),t=>Ok("{\"Answers\":[{\"Id\":1,\"Accepted\":false,\"Answer\":\"\",\"Reason\":\"已有说明\"}]}"),log);
   Check(result.Text==draft.Text&&log.FlowReviewStatus=="no_accepted_questions"&&log.FlowAnswers[0].Reason=="已有说明","rejected questions are logged but create no visible appendix");
   readers=0;log=new RepositoryExplanationLog();result=RepositoryExplanationReview.Review(draft,context,t=>++readers==1?Questions(""):Questions("会等待","下一轮怎么办？"),t=>Ok(Answers.Replace(",\"Reason\":\"\"","")),log);
   Check(log.FlowReviewStatus=="complete"&&result.Text==draft.Text+log.FlowAppendix,"accepted answer need not include unused empty rejection reason");
   bool cancelled=false;readers=0;log=new RepositoryExplanationLog();
   try{RepositoryExplanationReview.Review(draft,context,t=>{readers++;return Questions("");},author,log,()=>true);}catch(OperationCanceledException){cancelled=true;}
   Check(cancelled&&readers==0&&log.ReviewStatus=="cancelled","cancellation prevents review calls");
   readers=authors=0;bool stop=false;log=new RepositoryExplanationLog();
   try{RepositoryExplanationReview.Review(draft,context,t=>{readers++;if(readers==2)stop=true;return Questions("会等待","怎么办？");},t=>{authors++;return Ok(body);},log,()=>stop);}catch(OperationCanceledException){}
   Check(readers==2&&authors==1&&log.FlowReviewStatus=="cancelled","cancellation after second reader prevents answer generation");
   readers=authors=0;log=new RepositoryExplanationLog();
   result=RepositoryExplanationReview.Generate(new GraphReadingSnapshot(),context,t=>{authors++;return authors==1?draft:authors==2?Ok(body):Ok(Answers);},t=>++readers==1?Questions("候选请求","哪个变量？"):Questions("会等待","下一轮怎么办？"),log);
   Check(readers==2&&authors==3&&log.ReviewStatus=="complete"&&result.Text==body+log.FlowAppendix,"production generation path includes both rounds");
  }
 }
}
