using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
namespace ElymsPet {
 static class ChatControlsTests {
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static void Check(bool ok,string name){if(!ok)throw new Exception("Chat controls: "+name);Console.WriteLine("PASS chat controls: "+name);}
  sealed class Queue : IWorkScheduler {public readonly List<Action> Jobs=new List<Action>(),Posts=new List<Action>();public void QueueWork(Action action){Jobs.Add(action);}void IWorkScheduler.Queue(Action action){QueueWork(action);}public void Post(Action action){Posts.Add(action);}public void FlushPosts(){var copy=Posts.ToArray();Posts.Clear();foreach(var action in copy)action();}}
  public static void Run(string root){
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"chat-controls");
   try{
    var queue=new Queue();var state=new PetState{ApiKey="fixture-chat-key",ApiBase="http://127.0.0.1:1",WebEnabled=false,AutoLife=false};
    using(var services=new PetServices(state,queue,new PetFeedback(delegate{},delegate{},delegate{}),s=>false,Storage.Folder,false,new SystemClock())){
     var chat=services.Conversation;chat.Send("解释当前函数");var original=Array.Find(chat.Lines,l=>l.Speaker==ChatSpeaker.User);
     var captured=new GraphReadingSnapshot{TopicId="original-topic",NodeId="original-node"};var readingField=typeof(ChatLine).GetField("Reading",Private);readingField.SetValue(original,captured);
     Check(chat.CanStop&&chat.CanResend&&queue.Jobs.Count==1,"sending exposes stop and resend while work is queued");
     typeof(ChatService).GetMethod("ReportSourceProgress",Private).Invoke(chat,new object[]{"旧问题的 DSH 进度"});
     chat.LiveReply="未完成的半句";chat.Stop();Check(!chat.Busy&&chat.LiveReply==""&&chat.LastUserInput==original.Text&&chat.RecentTurns(100).Length==0,"stop preserves original and discards incomplete answer without committing history");
     queue.Jobs[0]();Check(queue.Posts.Count==1,"stopped queued request never runs network completion");
     chat.Resend(original);string hint=chat.LastHint;queue.FlushPosts();
     Check(chat.Busy&&chat.LastHint==hint&&!chat.LastHint.Contains("旧问题"),"old queued progress cannot overwrite a resent request");
     var sent=Array.FindLast(chat.Lines,l=>l.Speaker==ChatSpeaker.User);Check(sent.Text==original.Text&&ReferenceEquals(readingField.GetValue(sent),captured)&&queue.Jobs.Count==2,"resend uses exact text and original graph snapshot");
     chat.ResendLast();Check(chat.Busy&&queue.Jobs.Count==3,"resending during a reply stops it and queues exactly one replacement");
     int count=chat.Lines.Length;chat.Resend(new ChatLine(ChatSpeaker.Elyms,"foreign",""));Check(chat.Lines.Length==count,"assistant or foreign messages cannot be resent as user input");
     chat.Stop();foreach(var job in queue.Jobs)job();queue.FlushPosts();Check(!chat.Busy&&chat.RecentTurns(100).Length==0,"late workers cannot restore stopped replies");
     chat.Clear();Check(!chat.CanResend&&chat.LastUserInput==null,"clear removes stale resend target");
    }
    using(var h=new GraphAiCreationTests.Harness(true,Storage.Folder)){
     h.SetAddResponse("cancelled-node");bool callback=false;h.Service.EditGraphWithAi("补充模块介绍",h.Graph.Id,"worker-npu","npu",r=>callback=true);
     Check(h.Service.Busy,"graph edit starts before cancellation");h.Service.CancelGraphAi();h.Queue.Run();
     Check(!h.Service.Busy&&!callback&&!h.Graph.Nodes.Exists(n=>n.Id=="cancelled-node"),"cancelled graph edit cannot apply a late patch");
    }
   }finally{Storage.Folder=previous;}
  }
 }
}
