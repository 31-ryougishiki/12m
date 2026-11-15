using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
namespace ElymsPet {
 // Anchors refer to immutable Output offsets. Expanding never alters the prose.
 public sealed class RepositoryExplanationView : StackPanel {
  string text="";
  public string Text {get{return text;}set{text=value??"";Children.Clear();AddText(text);}}
  static GraphCopyText Label(string value){return new GraphCopyText{Text=value,FontFamily=Theme.ReadingFont,FontSize=14,Foreground=Theme.Brush("#33405B"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0)};}
  void AddText(string value){if(value.Length>0)Children.Add(Label(value));}
  public void Show(RepositoryExplanationLog saved){
   if(saved==null){Text="";return;}
   text=string.IsNullOrWhiteSpace(saved.DisplayText)?RepositoryExplanationLog.Format(new ChatOutcome{Ok=true,Text=saved.Output}):saved.DisplayText;
   Children.Clear();string body=saved.Output??"";
   if(saved.CodeAnchors==null||saved.CodeAnchors.Count==0){AddText(text);return;}
   AddText("AI 讲解 · 待核实\n");Children.Add(MessageText.CopyButton(body));int offset=0;
   foreach(var anchor in saved.CodeAnchors){
    if(anchor==null||anchor.Quote==null||anchor.Offset<offset||anchor.Offset+anchor.Quote.Length>body.Length||body.Substring(anchor.Offset,anchor.Quote.Length)!=anchor.Quote||anchor.Excerpts==null||anchor.Excerpts.Count==0)continue;
    AddText(body.Substring(offset,anchor.Offset-offset));var sentence=Label("");
    var link=new Hyperlink(new Run("代码 ▸ ")){Foreground=Theme.Brush("#2168AD"),Cursor=Cursors.Hand,ToolTip="原位展开代码上下文；黄色底色为该句对应的代码行"};
    System.Windows.Automation.AutomationProperties.SetName(link,"展开代码上下文 "+anchor.SentenceId);
    sentence.Inlines.Add(link);sentence.Inlines.Add(new Run(anchor.Quote));Children.Add(sentence);
    var code=new StackPanel();foreach(var excerpt in anchor.Excerpts)code.Children.Add(Context(excerpt));
    var panel=new Border{Child=code,Visibility=Visibility.Collapsed,Margin=new Thickness(0,6,0,10),Padding=new Thickness(8),Background=Theme.Brush("#F0F3F7"),BorderBrush=Theme.Brush("#CBD5E1"),BorderThickness=new Thickness(1)};
    link.Click+=delegate{bool open=panel.Visibility!=Visibility.Visible;panel.Visibility=open?Visibility.Visible:Visibility.Collapsed;link.Inlines.Clear();link.Inlines.Add(new Run(open?"代码 ▾ ":"代码 ▸ "));};Children.Add(panel);
    offset=anchor.Offset+anchor.Quote.Length;
   }
   AddText(body.Substring(offset));
   if(saved.CodeUnmapped!=null&&saved.CodeUnmapped.Count>0){
    var missing=new StackPanel();var sentences=RepositoryExplanationCodeReview.Sentences(body);
    foreach(var gap in saved.CodeUnmapped){var sentence=sentences.Find(s=>s.Id==gap.SentenceId);if(sentence!=null)missing.Children.Add(Label(sentence.Text+"\n未定位原因："+gap.Reason+"\n"));}
    Children.Add(new Expander{Header="尚未定位的代码对应 · "+saved.CodeUnmapped.Count,Content=missing,IsExpanded=false,Margin=new Thickness(0,12,0,0)});
   }
  }
  static UIElement Context(ExplanationCodeExcerpt excerpt){
   var box=new StackPanel{Margin=new Thickness(0,0,0,6)};
   if(excerpt==null||excerpt.Lines==null||excerpt.ContextStart<1)return box;
   box.Children.Add(new TextBlock{Text=excerpt.Path+" · "+(excerpt.Commit??"").Substring(0,Math.Min(12,(excerpt.Commit??"").Length))+" · L"+excerpt.HighlightStart+"–L"+excerpt.HighlightEnd,TextWrapping=TextWrapping.Wrap,Foreground=Theme.Brush("#52647D"),FontSize=12,Margin=new Thickness(0,0,0,6)});
   var lines=new StackPanel();
   for(int i=0;i<excerpt.Lines.Length;i++){
    int number=excerpt.ContextStart+i;bool highlighted=number>=excerpt.HighlightStart&&number<=excerpt.HighlightEnd;
    var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new TextBlock{Text=number.ToString(),Width=54,TextAlignment=TextAlignment.Right,Padding=new Thickness(0,0,10,0),FontFamily=new FontFamily("Consolas"),FontSize=13,Foreground=Theme.Brush("#718096")});
    row.Children.Add(new GraphCopyText{Text=excerpt.Lines[i],TextWrapping=TextWrapping.NoWrap,FontFamily=new FontFamily("Consolas"),FontSize=13,Foreground=Theme.Brush("#253149"),Margin=new Thickness(0)});
    lines.Children.Add(new Border{Tag=highlighted?"code-highlight":"code-context",Background=Theme.Brush(highlighted?"#FFF0B3":"#F0F3F7"),Padding=new Thickness(2,2,10,2),Child=row});
   }
   box.Children.Add(new ScrollViewer{Content=lines,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=420});return box;
  }
 }
}
