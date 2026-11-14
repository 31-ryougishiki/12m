using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace ElymsPet {
 static class ExperienceReviewTests {
  static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS experience: "+label);}
  public static void Run(string root){
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"experience");Directory.CreateDirectory(Storage.Folder);
   try{
    const string secret="fake-review-secret-12345";
    using(var file=File.Create(Storage.FilePath))new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PetState)).WriteObject(file,new PetState{ApiKey=secret,Note="legacy"});
    var state=Storage.Load();Check(state.ApiKey==secret,"legacy credential remains usable after migration");
    Check(!File.ReadAllText(Storage.FilePath).Contains(secret)&&!File.ReadAllText(Storage.FilePath+".bak").Contains(secret),"settings and backup contain no plaintext credential");
    state.Note="new";Check(Storage.Save(state),"encrypted settings save");File.WriteAllText(Storage.FilePath,"broken");
    var restored=Storage.Load();Check(restored.ApiKey==secret&&restored.Note=="legacy","encrypted backup restores credential and previous settings");
    Check(ChatService.VisiblePartial("hello [[MEMORY]]private")=="hello"&&ChatService.VisiblePartial("hello [")=="hello","partial output hides protocol markers across chunks");
    using(var services=new PetServices(new PetState{AiChat=false},new ImmediateScheduler(),new PetFeedback(delegate{},delegate{},delegate{}),delegate{return false;},Storage.Folder,true,new SystemClock())){
     services.Conversation.Send(new string('字',4001));Check(services.Conversation.Lines.Length==0&&services.Conversation.LastUserInput==null,"oversized input is rejected before history or model work");
     services.Conversation.Send("测试原文");Check(services.Conversation.LastUserInput=="测试原文","original input is retained for retry");
    }
    Check(PluginPanel.PageFor("cloud")==PanelTab.Cloud&&PluginPanel.PageFor("bili")==PanelTab.Bili,"optional plugin routes do not fall back to web");
    Stream(false);Stream(true);Cancel();
   }finally{Storage.Folder=previous;}
  }
  sealed class ImmediateScheduler:IWorkScheduler{public void Queue(Action action){action();}public void Post(Action action){action();}}
  static void Stream(bool incomplete){
   var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
   var server=Task.Run(delegate{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){
    ReadRequest(stream);
    string body="data: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\n\n";
    if(!incomplete)body+="data: {\"choices\":[{\"delta\":{\"content\":\"世界\"},\"finish_reason\":\"stop\"}],\"usage\":{\"completion_tokens\":4}}\n\ndata: [DONE]\n\n";
    byte[] data=Encoding.UTF8.GetBytes(body),header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: "+data.Length+"\r\nConnection: close\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(data,0,data.Length);
   }});
   try{string partial="";var result=DeepSeek.Complete("test-key-123","http://127.0.0.1:"+port,"test",false,new[]{new ChatTurn("user","hello")},5,100,0.3,"low",4000,value=>partial=value);
    Check(incomplete?!result.Ok:result.Ok&&result.Text=="你好世界"&&partial==result.Text&&result.CompletionTokens==4,incomplete?"interrupted stream cannot commit partial answer":"stream assembles Unicode chunks and token usage: "+result.Error+" / "+result.Detail+" / "+result.Text+" / "+partial);Check(server.Wait(3000),"local stream server completed");
   }finally{listener.Stop();}
  }
  static void ReadRequest(NetworkStream stream){
   // Read only headers; the response does not depend on the request body.
   int matched=0;var header=new StringBuilder();byte[] end={13,10,13,10};while(matched<4){int b=stream.ReadByte();if(b<0)throw new IOException();header.Append((char)b);matched=b==end[matched]?matched+1:(b==13?1:0);}
   int length=0;foreach(string line in header.ToString().Split('\n'))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))int.TryParse(line.Substring(15).Trim(),out length);for(int i=0;i<length;i++)if(stream.ReadByte()<0)throw new IOException();
  }
  static void Cancel(){
   var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
   using(var received=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var cts=new CancellationTokenSource()){
    var server=Task.Run(delegate{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){ReadRequest(stream);received.Set();release.Wait(5000);}});
    var request=Task.Run(()=>NetworkCancellation.Run(cts.Token,()=>DeepSeek.Complete("test-key-123","http://127.0.0.1:"+port,"test",false,new[]{new ChatTurn("user","hello")},30)));
    try{Check(received.Wait(3000),"request reached slow server");cts.Cancel();Check(request.Wait(2000)&&!request.Result.Ok,"cancellation aborts active HTTP before original timeout");}
    finally{release.Set();listener.Stop();server.Wait(3000);}
   }
  }
 }
}
