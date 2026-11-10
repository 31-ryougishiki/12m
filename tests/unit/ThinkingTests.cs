using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
namespace ElymsPet {
 static class ThinkingTests {
  static int checks;
  static void Check(bool pass,string name){if(!pass)throw new Exception(name);checks++;Console.WriteLine("PASS thinking: "+name);}
  static string Request(bool thinking,int tokens,string effort) {
   var build=typeof(DeepSeek).GetMethod("BuildRequest",BindingFlags.NonPublic|BindingFlags.Static);
   object request=build.Invoke(null,new object[]{"deepseek-flash",thinking,tokens,0.3,new[]{new ChatTurn("user","test")},effort});
   using(var stream=new MemoryStream()){new DataContractJsonSerializer(request.GetType()).WriteObject(stream,request);return Encoding.UTF8.GetString(stream.ToArray());}
  }
  public static void Run() {
   string high=Request(true,16384,"high");
   Check(high.Contains("\"type\":\"enabled\"")&&high.Contains("\"reasoning_effort\":\"high\"")&&high.Contains("\"max_tokens\":16384"),"wire request enables high reasoning without old 4096 token cap");
   Check(Request(true,2048,"low").Contains("\"reasoning_effort\":\"low\""),"normal chat retains low effort");
   string off=Request(false,350,"high");Check(off.Contains("\"type\":\"disabled\"")&&!off.Contains("reasoning_effort"),"fast planner omits reasoning effort");
   Check(Request(true,999999,"invalid").Contains("\"max_tokens\":16384")&&Request(true,8192,"invalid").Contains("\"reasoning_effort\":\"low\""),"invalid options stay bounded");
   var result=DeepSeek.ParseResponse("{\"choices\":[{\"message\":{\"content\":\"最终回答\",\"reasoning_content\":\"PRIVATE_REASONING_SENTINEL\"},\"finish_reason\":\"stop\"}],\"usage\":{\"completion_tokens\":90,\"completion_tokens_details\":{\"reasoning_tokens\":75}}}",1);
   Check(result.Ok&&result.HasReasoning&&result.ReasoningTokens==75&&result.Text=="最终回答"&&!result.Detail.Contains("PRIVATE_REASONING"),"reasoning metadata is available without exposing or storing thought text");
   string json="{\"choices\":[{\"message\":{\"content\":\""+new string('x',6000)+"\"},\"finish_reason\":\"stop\"}]}";
   Check(DeepSeek.ParseResponse(json,0).Truncated,"default chat display cap retained");
   var archive=DeepSeek.ParseResponse(json,0,24000);Check(!archive.Truncated&&archive.Text.Length==6000,"structured archive result no longer clipped by chat display limit");
   var empty=DeepSeek.ParseResponse("{\"choices\":[{\"message\":{\"content\":\"\",\"reasoning_content\":\"hidden\"},\"finish_reason\":\"length\"}]}",1);
   Check(!empty.Ok,"reasoning-only token exhaustion is not treated as a final answer");
   Console.WriteLine("PASS thinking: "+checks+" checks");
  }
 }
}
