using System;
using System.Collections.Generic;
namespace ElymsPet {
 static class RepositoryExplanationCodeTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS explanation code: "+name);}
  public static void Run(){
   const string body="## 流程\n\n先看队首 `queue.peek()`。然后保持队列不变。\n\n依据：test.py:3-4\n";
   var sentences=RepositoryExplanationCodeReview.Sentences(body);Check(sentences.Count==2&&body.Substring(sentences[0].Offset,sentences[0].Text.Length)==sentences[0].Text,"sentence offsets preserve original prose and skip headings/citation labels");
   var source=new ExplanationCodeSource{Id="f1",Path="test.py",Commit=new string('a',40),Url="https://github.com/a/b/blob/"+new string('a',40)+"/test.py",Lines=new[]{"# before","def choose(queue):","    request = queue.peek()","    return request","# after"},Included=new List<ExplanationCodeRange>{new ExplanationCodeRange{Start=1,End=5}}};
   var map=new ExplanationCodeMap{Mappings=new List<ExplanationCodeMapping>{new ExplanationCodeMapping{SentenceId=1,References=new List<ExplanationCodeReference>{new ExplanationCodeReference{SourceId="f1",StartLine=3,EndLine=3}}}},Unmapped=new List<ExplanationCodeGap>()};
   var anchors=RepositoryExplanationCodeReview.Validate(body,sentences,new List<ExplanationCodeSource>{source},map);
   Check(anchors[0].Quote==sentences[0].Text&&anchors[0].Excerpts[0].Lines[2]==source.Lines[2]&&anchors[0].Excerpts[0].HighlightStart==3&&anchors[0].Excerpts[0].ContextStart==1,"context copied from verified source and exact statement lines highlighted");
   var wideSource=new ExplanationCodeSource{Id="f1",Lines=new string[30],Included=new List<ExplanationCodeRange>{new ExplanationCodeRange{Start=1,End=30}}};
   map.Mappings[0].References[0].StartLine=1;map.Mappings[0].References[0].EndLine=21;bool tooWide=false;try{RepositoryExplanationCodeReview.Validate(body,sentences,new List<ExplanationCodeSource>{wideSource},map);}catch(InvalidOperationException){tooWide=true;}Check(tooWide,"whole-function highlights over twenty lines must be narrowed");
   map.Mappings[0].References[0].StartLine=map.Mappings[0].References[0].EndLine=3;
   var log=new RepositoryExplanationLog();var original=new ChatOutcome{Ok=true,Text=body};int calls=0;
   var result=RepositoryExplanationCodeReview.Run(original,new List<ExplanationCodeSource>{source},turns=>{calls++;Check(turns.Length==2&&turns[0].Content==RepositoryExplanationCodeReview.Rules&&turns[1].Content.Contains("L3:     request = queue.peek()")&&turns[1].Content.Contains("讲解原文"),"fresh third reader receives original prose and numbered raw code only");return new ChatOutcome{Ok=true,Text=GraphEditing.Json(map)};},log);
   Check(calls==1&&result.Text==body&&log.CodeReviewStatus=="complete"&&log.CodeAnchors.Count==1,"third round attaches references without rewriting body or appendix");
   var restored=GraphEditing.Parse<RepositoryExplanationLog>(GraphEditing.Json(log));Check(restored.CodeAnchors[0].Excerpts[0].Lines[2]==source.Lines[2],"saved code context survives restart without requiring working-tree files");
   map.Mappings.Add(new ExplanationCodeMapping{SentenceId=2,References=new List<ExplanationCodeReference>{new ExplanationCodeReference{SourceId="f1",StartLine=99,EndLine=99}}});
   var partial=new RepositoryExplanationLog();RepositoryExplanationCodeReview.Run(original,new List<ExplanationCodeSource>{source},t=>new ChatOutcome{Ok=true,Text=GraphEditing.Json(map)},partial);
   Check(partial.CodeReviewStatus=="partial"&&partial.CodeAnchors.Count==1&&partial.CodeUnmapped.Count==1&&partial.CodeUnmapped[0].SentenceId==2,"invalid mapping becomes an explicit gap without discarding independently verified entries");map.Mappings.RemoveAt(1);
   foreach(int invalid in new[]{0,6}){map.Mappings[0].References[0].StartLine=map.Mappings[0].References[0].EndLine=invalid;bool rejected=false;try{RepositoryExplanationCodeReview.Validate(body,sentences,new List<ExplanationCodeSource>{source},map);}catch(InvalidOperationException){rejected=true;}Check(rejected,"unprovided line numbers are rejected");}
   map.Mappings[0].References[0].StartLine=map.Mappings[0].References[0].EndLine=3;source.Included=new List<ExplanationCodeRange>{new ExplanationCodeRange{Start=1,End=2}};bool gap=false;try{RepositoryExplanationCodeReview.Validate(body,sentences,new List<ExplanationCodeSource>{source},map);}catch(InvalidOperationException){gap=true;}Check(gap,"existing file lines omitted from model input cannot be cited");
   log=new RepositoryExplanationLog();result=RepositoryExplanationCodeReview.Run(original,new List<ExplanationCodeSource>(),t=>{throw new Exception("must not call");},log);Check(result.Text==body&&log.CodeReviewStatus=="failed","missing source preserves text and explicitly reports code review failure");
   bool cancelled=false;try{RepositoryExplanationCodeReview.Run(original,new List<ExplanationCodeSource>{source},t=>{throw new Exception("must not call");},new RepositoryExplanationLog(),()=>true);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancellation prevents third model call");
  }
 }
}
