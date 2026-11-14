using System.Collections.Generic;
namespace ElymsPet {
 public static class WebAnswerCompletion {
  public static ChatOutcome Complete(IArchiveApi api,string key,string url,string model,ChatTurn[] turns){
   var input=new List<ChatTurn>(turns);
   for(int attempt=0;attempt<2;attempt++){
    var result=api.Complete(key,url,model,true,input.ToArray(),150,attempt==0?8192:16384,0.3);
    if(result==null||!result.Ok)return result;
    if(!result.Truncated&&!string.IsNullOrWhiteSpace(result.Text))return result;
    input.Add(new ChatTurn("user","上次回答被截断或为空。请使用已有资料重新给出完整结果，保留所有关键步骤、命令、验收与缺口；精简铺垫，不再重复读取网页。不要把未完成的回答当成已完成。"));
   }
   return ChatOutcome.Fail("资料已读取，但整理回答仍未完整结束，请重试；未将半份流程当成完成结果。","incomplete web answer",200,0);
  }
 }
}
