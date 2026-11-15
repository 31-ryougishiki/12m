using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using ElymsPet;
public static class FlowLiveProbe {
 public static int Main(string[] args) {
  try {
   var patch=GraphEditing.Parse<GraphEditRequest>(File.ReadAllText(args[0]));var node=patch.Nodes.Find(n=>n.Id=="scheduler-schedule");
   var context=new[]{new ChatTurn("system","Rewrite Summary and Approach using supplied evidence only. "+ReadableExplanation.StructureRules),new ChatTurn("user",File.ReadAllText(args[1]))};
   Run(Storage.Load(),node.Summary,node.Approach,context,args[2]);Console.WriteLine("PASS live two-round review: "+args[2]);return 0;
  }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
 }
 public static void Run(PetState state,string summary,string approach,ChatTurn[] context,string folder) {
  var api=new ArchiveApi();var key=ApiKey.Resolve(state);var trace=new List<string>();
  try {
   var result=FlowReaderReview.Text("Scheduler.schedule",summary,approach,context,
    turns=>{
     if(turns.Length!=2||turns[1].Content.Contains("FILE vllm/"))throw new Exception("Reader isolation failed");
     var r=FlowReaderReview.CompleteReader(api,key.Key,state.ApiBase,state.ApiModel,turns);
     trace.Add("reader input: "+turns[1].Content+"\nreader output: "+r.Text);return r;
    },
    turns=>{var r=FlowReaderReview.CompleteAuthor(api,key.Key,state.ApiBase,state.ApiModel,turns);trace.Add("author output: "+r.Text);return r;});
   if(trace.Count!=4)throw new Exception("Expected two reader/author rounds");
   File.WriteAllText(Path.Combine(folder,"final.json"),GraphEditing.Json(result),new UTF8Encoding(false));
  }finally{File.WriteAllText(Path.Combine(folder,"trace.json"),GraphEditing.Json(trace),new UTF8Encoding(false));}
 }
}