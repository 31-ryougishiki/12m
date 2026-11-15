using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace ElymsPet {
 static class MessageTextTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Message text: "+name);Console.WriteLine("PASS message text: "+name);}
  static bool Reject(Action action){try{action();return false;}catch{return true;}}
  public static void Run(string root){
   string target="D:/code/elyms-learning/vllm/vllm/v1/core/sched/scheduler.py#L194-L196";
   string original="（[scheduler.py:194-196]("+target+")）。没有独立开关。";
   var parts=MessageLinks.Parse(original);Check(parts.Count==3&&parts[1].Text=="scheduler.py:194-196"&&parts[1].Target==target&&parts[2].Text=="）。没有独立开关。","screenshot citation renders as a link without losing adjacent Chinese text");
   string file;int line;Check(MessageLinks.TryLocal(target,out file,out line)&&file==@"D:\code\elyms-learning\vllm\vllm\v1\core\sched\scheduler.py"&&line==194,"line range opens its first line");
   Check(MessageLinks.TryLocal(@"D:\code\项目 (main)\a.py:23:4",out file,out line)&&line==23,"Windows paths with spaces, parentheses and column suffix are supported");
   Check(MessageLinks.Parse("[源码](D:/code/项目 (main)/a.py#L9)").Find(p=>p.Target!=null).Target=="D:/code/项目 (main)/a.py#L9","markdown path parentheses remain inside the destination");
   Check(MessageLinks.TryLocal("file:///D:/code/%E9%A1%B9%E7%9B%AE/a.py#L4",out file,out line)&&file.Contains("项目")&&line==4,"file URI decodes Unicode and preserves line number");
   var bare=MessageLinks.Parse("位置 D:/code/repo/a.py:12-15；网页 https://example.org/code#L2。");Check(bare.FindAll(p=>p.Target!=null).Count==2&&bare.Find(p=>p.Target!=null).Target.EndsWith(":12-15"),"bare local paths and web links retain punctuation outside the link");
   foreach(string invalid in new[]{"javascript:alert(1)","cmd.exe /c start",@"\\server\share\a.py","file://server/share/a.py","D:/repo/../secret.py#L2","D:/repo/a.py#L0","D:/repo/a.py#L8-L2","D:/repo/a.py:secret","D:/repo/a.py#L999999999999999"})Check(!MessageLinks.Supported(invalid),"unsafe or invalid destination is not clickable: "+invalid);
   string folder=Path.Combine(root,"代码 (test)");Directory.CreateDirectory(folder);string source=Path.Combine(folder,"source.py"),editor=Path.Combine(folder,"Code.exe");File.WriteAllText(source,"one\ntwo\n");File.WriteAllText(editor,"");var settings=new LocalRepositorySettings{Editor=editor};
   var info=MessageLinks.OpenInfo(source+"#L2",settings);Check(info.FileName==editor&&!info.UseShellExecute&&info.Arguments.Contains(source+":2:1\""),"local source opens only in configured editor with quoted goto line");
   Check(Reject(()=>MessageLinks.OpenInfo(Path.Combine(folder,"missing.py")+"#L1",settings)),"missing source is rejected before editor launch");
   Check(Reject(()=>MessageLinks.OpenInfo("javascript:alert(1)",settings)),"unsupported protocol cannot be launched");
   Check(MessageLinks.OpenInfo("https://github.com/org/repo",settings).UseShellExecute,"web links use the default browser");
   string opened=null;var body=new MessageText(original,Brushes.Black,14,s=>opened=s);var paragraph=(Paragraph)body.Document.Blocks.FirstBlock;Hyperlink link=null;foreach(Inline inline in paragraph.Inlines)if(inline is Hyperlink)link=(Hyperlink)inline;
   Check(body.IsReadOnly&&body.IsDocumentEnabled&&link!=null&&body.OriginalText==original,"selectable message keeps original Markdown for complete copying");
   link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));Check(opened==target,"inline click dispatches the exact code destination");
   body.SelectAll();Check(body.Selection.Text.Contains("scheduler.py:194-196")&&!body.Selection.Text.Contains("]("),"selected text copies rendered readable content");
   string copied=null;var button=MessageText.CopyButton(original,s=>{copied=s;return true;});button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(copied==original&&button.Content as string=="已复制","copy entire message preserves original text and full code path");
   button=MessageText.CopyButton(original,s=>false);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(button.Content as string=="复制失败，请重试","clipboard failure never reports copied success");
  }
 }
}
