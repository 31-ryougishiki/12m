using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace ElymsPet {
 static class ChatActivityTests {
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static void Check(bool ok,string name){if(!ok)throw new Exception("Chat activity: "+name);Console.WriteLine("PASS chat activity: "+name);}
  static T Field<T>(object target,string name){return (T)target.GetType().GetField(name,Private).GetValue(target);}
  static void Call(object target,string method,params object[] args){target.GetType().GetMethod(method,Private).Invoke(target,args);}
  static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
  public static void Run(PetWindow pet,ControlWindow panel,ChatBarWindow bar,Action<FrameworkElement,string> snapshot){
   var chat=pet.Conversation;var busy=typeof(ChatService).GetField("busy",Private);int history=chat.RecentTurns(100).Length;
   var main=Field<ChatView>(panel,"chatView");var floating=Field<ChatView>(bar,"view");
   panel.SelectTab(PanelTab.Companion);
   try{
    busy.SetValue(chat,true);Call(chat,"ReportSourceProgress","DSH 正在阅读本地源码…");Pump();
    foreach(var view in new[]{main,floating}){var stack=Field<StackPanel>(view,"stack");var activity=Field<Border>(view,"activity");Check(stack.Children.Contains(activity)&&((TextBlock)activity.Child).Text.Contains("正在思考")&&((TextBlock)activity.Child).Text.Contains("DSH"),"main and floating chat show persistent DSH thinking feedback");}
    Check(Field<TextBlock>(bar,"headerStatus").Text.Contains("DSH")&&Field<TextBlock>(panel,"aiStatus").Text.Contains("DSH"),"headers reflect DSH phase instead of fixed connection text");
    string announcement=Field<TextBlock>(pet,"bubbleText").Text;Check(announcement.Contains("正让 DSH"),"pet announces the source handoff");
    int count=Field<StackPanel>(main,"stack").Children.Count;
    for(int i=0;i<4;i++)Call(chat,"ReportSourceProgress","DSH 正在阅读本地源码 · 工具 "+i+" 次");Pump();
    Check(Field<StackPanel>(main,"stack").Children.Count==count&&Field<TextBlock>(main,"activityText").Text.Contains("工具 3 次")&&Field<TextBlock>(pet,"bubbleText").Text==announcement,"polling updates one indicator without repeated bubbles or chat messages");
    typeof(PetWindow).GetField("stateUntil",Private).SetValue(pet,0d);Call(pet,"Tick",null,EventArgs.Empty);Check(pet.Art.Frame==6&&Field<TextBlock>(pet,"badgeText").Text.Contains("正在思考"),"thinking animation survives the original timed speech expiry");
    panel.UpdateLayout();bar.UpdateLayout();Pump();snapshot(panel,"dsh-thinking-panel");snapshot(bar,"dsh-thinking-bar");
    Call(chat,"ReportSourceProgress","DSH 已完成源码阅读，正在整理回答…");Pump();Check(Field<TextBlock>(main,"activityText").Text.Contains("整理回答"),"source completion transitions to answer synthesis");
    Check(chat.RecentTurns(100).Length==history,"activity is not saved as assistant conversation history");
    Call(chat,"OnFailure","测试失败状态");Pump();
    foreach(var view in new[]{main,floating})Check(!Field<StackPanel>(view,"stack").Children.Contains(Field<Border>(view,"activity")),"failure clears the transient thinking indicator");
    Call(chat,"ReportSourceProgress","迟到的进度");Pump();Check(chat.LastHint=="测试失败状态","late progress cannot revive a finished request");
    typeof(PetWindow).GetField("forced",Private).SetValue(pet,-1);Call(pet,"Tick",null,EventArgs.Empty);Check(pet.Art.Frame!=6,"pet leaves thinking state when work ends");
   }finally{busy.SetValue(chat,false);main.SetActivity("");floating.SetActivity("");}
  }
 }
}
