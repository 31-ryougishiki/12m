using System;
using System.Collections.Generic;
namespace ElymsPet {
 static class FlowReaderReviewTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Flow reader: "+name);Console.WriteLine("PASS flow reader: "+name);}
  static ChatOutcome Ok(string text){return new ChatOutcome{Ok=true,Text=text};}
  public static void Run(string root){
   string conditionText="空间不足时，调度器抢占请求。";
   var conditionReview=new FlowReaderReview.ReaderResult{Questions=new List<string>(),Decisions=new List<FlowReaderReview.DecisionCheck>{new FlowReaderReview.DecisionCheck{Action="抢占",Trigger="空间不足时",Missing=true,Question="哪次资源申请失败才会触发抢占？"}}};
   bool rejected=false;try{FlowReaderReview.ValidateReader(conditionReview,conditionText);}catch{rejected=true;}
   Check(rejected,"missing behavior condition must produce an explicit reader question");
   conditionReview.Questions.Add(conditionReview.Decisions[0].Question);FlowReaderReview.ValidateReader(conditionReview,conditionText);
   Check(true,"vague condition can be quoted while still flagged incomplete");
   conditionReview.Decisions[0].Missing=false;conditionReview.Decisions[0].Trigger="";rejected=false;
   try{FlowReaderReview.ValidateReader(conditionReview,conditionText);}catch{rejected=true;}
   Check(rejected,"a claimed complete condition needs textual evidence");
   conditionReview.Decisions[0].Trigger="allocate_slots 返回 None";rejected=false;
   try{FlowReaderReview.ValidateReader(conditionReview,conditionText);}catch{rejected=true;}
   Check(rejected,"reader cannot invent source-derived conditions absent from the flow");
   int readers=0,writers=0;var readerTexts=new List<string>();
   var context=new[]{new ChatTurn("system","author-only SOURCE_SENTINEL"),new ChatTurn("user","original task")};
   Func<ChatTurn[],ChatOutcome> reader=turns=>{
    readers++;Check(turns.Length==2&&turns[0].Content==FlowReaderReview.ReaderRules&&!turns[1].Content.Contains("SOURCE_SENTINEL"),"child receives only its isolated reading context");readerTexts.Add(turns[1].Content);
    if(readers==2)Check(turns[1].Content.Contains("谁接收这个结果？")&&turns[1].Content.Contains("第二轮"),"second round explicitly revisits unanswered questions without author source context");
    return Ok(readers%2==1?"{\"Questions\":[\"谁接收这个结果？\"],\"Decisions\":[]}":"{\"Questions\":[],\"Decisions\":[]}");
   };
   Func<ChatTurn[],ChatOutcome> writer=turns=>{
    writers++;Check(turns[0].Content.Contains("SOURCE_SENTINEL")&&turns[turns.Length-1].Content.Contains("Questions"),"author retains evidence and receives reader questions");
    return Ok("{\"Summary\":\"修订"+writers+"\",\"Approach\":\"由执行器接收计划。\",\"Code\":\"must not propagate\"}");
   };
   var revised=FlowReaderReview.Text("流程","初稿","未说明接收方",context,reader,writer);
   Check(readers==2&&writers==2&&revised.Summary=="修订2"&&readerTexts[1].Contains("修订1")&&!readerTexts[1].Contains("未说明接收方"),"exactly two question/revision rounds; second reader sees the revised text even when no further questions");
   var patch=new GraphEditRequest{TopicId="topic",ExpectedRevision="version",Nodes=new List<DomainNode>{new DomainNode{Id="a",Title="A",Summary="流程A",Approach="步骤",Code="SOURCE_SENTINEL",Sources="PRIVATE_SOURCE_URL"},new DomainNode{Id="b",Title="B",Summary="流程B",Approach="步骤"}},Modules=new List<GraphModulePatch>{new GraphModulePatch{Id="module",Summary="模块流程"}},Edges=new List<DomainEdge>{new DomainEdge{From="a",To="b"}}};
   readers=writers=0;readerTexts.Clear();string edges=GraphEditing.Json(patch.Edges);
   var result=FlowReaderReview.Parse<GraphEditRequest>(FlowReaderReview.Patch(Ok(GraphEditing.Json(patch)),null,context,reader,writer));
   Check(readers==6&&writers==6&&result.Nodes[0].Code=="SOURCE_SENTINEL"&&result.Nodes[0].Sources=="PRIVATE_SOURCE_URL"&&result.ExpectedRevision=="version"&&GraphEditing.Json(result.Edges)==edges,"each node and module receives two rounds without changing protected fields");
   Check(readerTexts.TrueForAll(t=>!t.Contains("PRIVATE_SOURCE_URL"))&&!readerTexts[0].Contains("流程B")&&!readerTexts[2].Contains("流程A"),"no sources or sibling flow drafts leak into a child");
   bool failed=false;int calls=0;
   try{FlowReaderReview.Text("流程","草稿","步骤",context,t=>Ok("{}"),t=>{calls++;return Ok("{}");});}catch{failed=true;}
   Check(failed&&calls==0,"malformed questions stop before revision");
   failed=false;try{FlowReaderReview.Text("流程","草稿","步骤",context,t=>new ChatOutcome{Ok=true,Text="{\"Questions\":[],\"Decisions\":[]}",Truncated=true},writer);}catch{failed=true;}
   Check(failed,"truncated reader response cannot finalize a flow");
   failed=false;calls=0;try{FlowReaderReview.Text("流程","草稿","步骤",context,t=>{calls++;return Ok("{\"Questions\":[],\"Decisions\":[]}");},writer,false,()=>true);}catch(OperationCanceledException){failed=true;}
   Check(failed&&calls==0,"cancellation prevents further child calls");
   using(var h=new GraphAiCreationTests.Harness(true,root)){
    h.SetAddResponse("reviewed");GraphEditOutcome outcome=null;h.Service.EditGraphWithAi("新增流程",h.Graph.Id,"worker-npu","npu",r=>outcome=r);h.Queue.Run();
    Check(outcome.Success&&h.Api.Calls==6&&h.Graph.Nodes.Exists(n=>n.Id=="reviewed"),"real edit entry runs planning, draft, and two child/author rounds before saving");
    string before=GraphEditing.Revision(h.Graph);h.SetAddResponse("must-not-save");h.Api.FailReaderAt=h.Api.ReaderCalls+2;
    h.Service.EditGraphWithAi("新增流程",h.Graph.Id,"worker-npu","npu",r=>outcome=r);h.Queue.Run();
    Check(!outcome.Success&&!h.Service.Busy&&GraphEditing.Revision(h.Graph)==before,"second-round failure never saves a first-round draft or leaves the editor busy");
   }
  }
 }
}
