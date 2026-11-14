using System;
using System.Collections.Generic;
namespace ElymsPet {
 static class GraphCompletionTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS graph completion: "+name);}
  sealed class Api:IArchiveApi,IGraphCompletionApi {
   public readonly List<int> Budgets=new List<int>();public string Retry;public bool AlwaysTruncated;
   public ChatOutcome CompleteGraph(string key,string url,string model,ChatTurn[] turns,int tokens){
    Budgets.Add(tokens);Retry=turns[turns.Length-1].Content;
    if(Budgets.Count==1||AlwaysTruncated)return new ChatOutcome{Ok=true,Text="{\"Modules\":[",Truncated=true,FinishReason="length",CompletionTokens=tokens};
    return new ChatOutcome{Ok=true,Text="{\"Modules\":[]}",FinishReason="stop"};
   }
   public ChatOutcome Complete(string a,string b,string c,bool d,ChatTurn[] e,int f,int g,double h){throw new Exception("Must use graph response limit");}
   public WebOutcome Search(string a,string b,string c){throw new Exception();}
   public WebOutcome Fetch(string a,string b,string c){throw new Exception();}
  }
  public static void Run(string root){
   string old=Storage.Folder;Storage.Folder=System.IO.Path.Combine(root,"graph-completion");
   try{
    var api=new Api();var result=GraphCompletion.Complete(api,"unused","unused","unused",new[]{new ChatTurn("user","展开子模块")});
    Check(result.Ok&&!result.Truncated&&api.Budgets.Count==2&&api.Budgets[0]==32768&&api.Budgets[1]==65536&&api.Retry.Contains("增量"),"truncated patch retries with increased budget and compact delta instructions");
    api=new Api{AlwaysTruncated=true};result=GraphCompletion.Complete(api,"unused","unused","unused",new[]{new ChatTurn("user","展开")});
    Check(!result.Ok&&api.Budgets.Count==2&&result.Error.Contains("原图谱未修改"),"exhausted retries return failure rather than partial JSON");
    string longJson=GraphEditing.Json(new GraphEditRequest{RequestId="large",TopicId="topic",Modules=new List<GraphModulePatch>{new GraphModulePatch{Id="child",Title="子模块",Repository="repo",Summary=new string('x',25000)}}});
    string wire="{\"choices\":[{\"message\":{\"content\":"+GraphEditing.Json(longJson)+"},\"finish_reason\":\"stop\"}]}";
    var legacy=DeepSeek.ParseResponse(wire,0,24000);var graph=DeepSeek.ParseResponse(wire,0,262144);
    Check(legacy.Truncated&&legacy.ResponseClipped&&legacy.FinishReason=="stop","reproduces local truncation of a complete provider response above 24000 characters");
    Check(!graph.Truncated&&graph.Text==longJson&&GraphEditing.Parse<GraphEditRequest>(graph.Text).Modules.Count==1,"structured graph response retains complete long JSON");
    Check(DeepSeek.ParseResponse(wire,0).Truncated,"normal chat retains its default display bound");
   }finally{Storage.Folder=old;}
  }
 }
}
