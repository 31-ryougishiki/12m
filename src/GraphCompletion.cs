using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
namespace ElymsPet {
 // Graph patches are structured data, not chat display text. Retry only model
 // generation against the same evidence; never replay DSH or apply a partial patch.
 public static class GraphCompletion {
  public static ChatOutcome Complete(IArchiveApi api,string key,string url,string model,ChatTurn[] turns) {
   bool planning=turns.Length>0&&turns[0].Content==DshSourceResearch.EditPlanRules;
   var input=new List<ChatTurn>(turns);ChatOutcome result=null;
   string id=Guid.NewGuid().ToString("N");int budget=planning?4096:32768;
   for(int attempt=0;attempt<2;attempt++) {
    try {var graphApi=api as IGraphCompletionApi;result=graphApi!=null?graphApi.CompleteGraph(key,url,model,input.ToArray(),budget):api.Complete(key,url,model,true,input.ToArray(),240,budget,0.1);}
    catch(WebException){result=ChatOutcome.Fail("图谱请求网络中断。","network",0,0);}
    catch(IOException){result=ChatOutcome.Fail("图谱请求读取中断。","read",0,0);}
    Trace(id,planning,attempt,budget,result);
    if(Valid(result))return result;
    if(result!=null&&!result.Ok&&result.Status!=0&&result.Status!=200&&result.Status!=408&&result.Status!=429&&result.Status<500)return result;
    if(attempt==0){budget=planning?8192:65536;input.Add(new ChatTurn("user",planning?"上次输出不完整。只返回完整的 RequiresSource、Question JSON。":"上次编辑结果未完整输出。请重新生成一个完整 GraphEditRequest JSON，只返回本次改变的增量字段；不要重复整张图谱、Architecture、Journey 或未修改节点。保留所请求的子模块、节点归属和必要证据，简洁描述，省略重复解释。不要续写上次残片。"));}
   }
   if(result!=null&&!result.Ok)return result;
   return ChatOutcome.Fail("图谱"+(planning?"源码核实规划":"修改结果")+"未完整生成，已提高输出额度重试一次。原图谱未修改，请缩小本次展开范围。", "graph completion incomplete",200,result==null?0:result.Seconds);
  }
  static bool Valid(ChatOutcome result) {
   if(result==null||!result.Ok||result.Truncated||string.IsNullOrWhiteSpace(result.Text))return false;
   object value;return MiniJson.TryParse(result.Text,out value)&&value is Dictionary<string,object>;
  }
  static void Trace(string id,bool planning,int attempt,int budget,ChatOutcome result) {
   try {string folder=Path.Combine(Storage.Folder,"graph-edit-diagnostics");Directory.CreateDirectory(folder);
    // Metadata only: no prompts, private graph content, credentials or reasoning text.
    string line=DateTime.UtcNow.ToString("o")+" request="+id+" stage="+(planning?"plan":"patch")+" attempt="+(attempt+1)+" budget="+budget;
    if(result!=null)line+=" ok="+result.Ok+" finish="+result.FinishReason+" truncated="+result.Truncated+" localClip="+result.ResponseClipped+" chars="+result.ResponseChars+" completionTokens="+result.CompletionTokens+" reasoningTokens="+result.ReasoningTokens;
    File.AppendAllText(Path.Combine(folder,DateTime.UtcNow.ToString("yyyyMMdd")+".log"),line+Environment.NewLine,new UTF8Encoding(false));
   }catch{}
  }
 }
}
