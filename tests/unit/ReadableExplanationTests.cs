using System;
namespace ElymsPet {
 static class ReadableExplanationTests {
  static void Check(bool ok,string label){if(!ok)throw new Exception("Readable explanation: "+label);Console.WriteLine("PASS readable explanation: "+label);}
  public static void Run(string root){Check(ReadableExplanation.MissingTerms("asyncio EngineCore stop-string abort",new ReadableExplanation.Draft{Approach="asyncio：说明；EngineCore：说明；abort：说明"})=="stop-string","omitted original terminology is detected instead of hidden behind Chinese replacements");using(var h=new GraphAiCreationTests.Harness(true,root)){
   var node=h.Graph.Nodes.Find(n=>n.Id=="worker-npu");string code=node.Code,sources=node.Sources,edges=GraphEditing.Json(h.Graph.Edges),map=GraphEditing.Json(h.Graph.Architecture);GraphEditOutcome result=null;
   h.Api.Results.Enqueue("{\"Summary\":\"初稿\",\"Approach\":\"初稿步骤\"}");h.Api.Results.Enqueue("{\"Summary\":\"审读后的中文说明\",\"Approach\":\"执行者接收输入，并把结果交给指定组件。\",\"Code\":\"不得保存的代码\",\"Title\":\"不得改名\"}");
   h.Service.RewriteNodeExplanation(h.Graph,node,r=>result=r);h.Queue.Run();var saved=h.Graph.Nodes.Find(n=>n.Id==node.Id);
   Check(result.Success&&h.Api.Calls==2&&saved.Summary=="审读后的中文说明","API drafts and reviews the explanation before it is persisted");
   Check(saved.Code==code&&saved.Sources==sources&&saved.Title==node.Title&&GraphEditing.Json(h.Graph.Edges)==edges&&GraphEditing.Json(h.Graph.Architecture)==map,"extra API fields cannot alter code, sources, titles or relationships");
   Check(h.Api.Prompt.Contains(ReadableExplanation.Rules)&&!RepositoryLearning.Prompt(h.Graph,saved,"").Contains(ReadableExplanation.Rules)&&RepositoryLearning.Prompt(h.Graph,saved,"").Contains(SourceResearch.AnswerRules)&&GraphEditing.Instructions(h.Service.Slot).Contains(ReadableExplanation.Rules),"code Q&A stays concise while explicit beginner rewrites retain their policy");
   string before=GraphEditing.Revision(h.Graph);h.Api.Result="{\"Summary\":\"缺少实现说明\"}";h.Service.RewriteNodeExplanation(h.Graph,saved,r=>result=r);h.Queue.Run();Check(!result.Success&&GraphEditing.Revision(h.Graph)==before&&!h.Service.Busy,"incomplete draft preserves the previous explanation and releases the task");
   h.Service.Slot.DomainGraphs[0].Nodes.Find(n=>n.Id==node.Id).Summary="asyncio EngineCore abort";var termsNode=h.Graph.Nodes.Find(n=>n.Id==node.Id);int calls=h.Api.Calls;
   h.Api.Results.Enqueue("{\"Summary\":\"初稿\",\"Approach\":\"遗漏术语\"}");h.Api.Results.Enqueue("{\"Summary\":\"审读稿\",\"Approach\":\"仍遗漏术语\"}");h.Api.Results.Enqueue("{\"Summary\":\"修订说明\",\"Approach\":\"asyncio：说明；EngineCore：说明；abort：说明 的中文作用说明\"}");h.Service.RewriteNodeExplanation(h.Graph,termsNode,r=>result=r);h.Queue.Run();Check(result.Success&&h.Api.Calls==calls+3,"missing original terminology triggers one bounded API repair before saving");saved=h.Graph.Nodes.Find(n=>n.Id==node.Id);
   h.Api.Result="{\"Summary\":\"新说明\",\"Approach\":\"新步骤\"}";h.Service.RewriteNodeExplanation(h.Graph,saved,r=>result=r);h.Service.Slot.DomainGraphs[0].Nodes[0].Summary="并发编辑";h.Queue.Run();Check(!result.Success&&h.Graph.Nodes.Find(n=>n.Id==node.Id).Summary==saved.Summary,"concurrent edits reject a stale rewrite");
  }}
 }
}
