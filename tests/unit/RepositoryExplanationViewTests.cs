using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace ElymsPet {
 static class RepositoryExplanationViewTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS explanation view: "+name);}
  public static void Run(){
   const string body="先选一个请求。只看队首，不移除。\n后续处理。";
   var saved=RepositoryExplanationStoreTests.Record(text:body);saved.CodeAnchors=new System.Collections.Generic.List<ExplanationCodeAnchor>{new ExplanationCodeAnchor{SentenceId=2,Offset=8,Quote="只看队首，不移除。",Excerpts=new System.Collections.Generic.List<ExplanationCodeExcerpt>{new ExplanationCodeExcerpt{Path="scheduler.py",Commit=new string('a',40),ContextStart=10,HighlightStart=11,HighlightEnd=11,Lines=new[]{"# context","request = queue.peek()","return request"}}}}};
   saved.CodeAnchors[0].Offset=body.IndexOf(saved.CodeAnchors[0].Quote,StringComparison.Ordinal);
   var view=new RepositoryExplanationView();view.Show(saved);
   var sentence=view.Children.OfType<TextBlock>().First(t=>t.Inlines.OfType<Hyperlink>().Any());var link=sentence.Inlines.OfType<Hyperlink>().Single();
   var panel=view.Children.OfType<Border>().Single();Check(panel.Visibility==Visibility.Collapsed&&sentence.Inlines.FirstInline==link,"code entry precedes the exact sentence and starts collapsed");
   link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));Check(panel.Visibility==Visibility.Visible&&view.Text==saved.DisplayText,"click expands context in place without changing explanation text");
   var code=(StackPanel)panel.Child;var excerpt=(StackPanel)code.Children[0];var lines=(StackPanel)((ScrollViewer)excerpt.Children[1]).Content;
   Check(lines.Children.OfType<Border>().Count(b=>(string)b.Tag=="code-highlight")==1&&lines.Children.OfType<Border>().Count(b=>(string)b.Tag=="code-context")==2,"only corresponding code line has highlight background; context has separate background");
   Check(((SolidColorBrush)((Border)lines.Children[0]).Background).Color!=((SolidColorBrush)((Border)lines.Children[1]).Background).Color,"highlight and context colors differ");
   link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));Check(panel.Visibility==Visibility.Collapsed,"second click collapses inline context");
   saved.CodeAnchors[0].Offset=999;view.Show(saved);Check(!view.Children.OfType<TextBlock>().Any(t=>t.Inlines.OfType<Hyperlink>().Any()),"stale annotation offsets cannot attach code to another sentence");
  }
 }
}
