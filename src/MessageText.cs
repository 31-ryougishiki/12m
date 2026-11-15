using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
namespace ElymsPet {
 public sealed class MessagePart {public string Text,Target;}
 public static class MessageLinks {
  static readonly Regex Links=new Regex(@"\[(?<label>[^\]\r\n]+)\]\((?<target>(?:[^()\r\n]|\([^()\r\n]*\))+)\)|(?<bare>(?:https?://|file:///|[A-Za-z]:[\\/])[^\s<>""`\[\]()，。；]+)");
  public static bool TryLocal(string value,out string file,out int line){
   file="";line=1;value=(value??"").Trim().Trim('<','>');
   if(value.StartsWith("file:///",StringComparison.OrdinalIgnoreCase)){Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||!uri.IsFile||uri.IsUnc||uri.Host.Length>0)return false;value=uri.LocalPath+uri.Fragment;}
   var match=Regex.Match(value,@"^(?<file>[A-Za-z]:[\\/].*?)(?:(?:#L?|:)(?<line>[1-9][0-9]*)(?:-L?(?<end>[1-9][0-9]*))?(?::[1-9][0-9]*)?)?$");
   if(!match.Success)return false;
   if(match.Groups["line"].Success&&!int.TryParse(match.Groups["line"].Value,out line))return false;
   int end;if(match.Groups["end"].Success&&(!int.TryParse(match.Groups["end"].Value,out end)||end<line))return false;
   try{string path=Uri.UnescapeDataString(match.Groups["file"].Value).Replace('/',Path.DirectorySeparatorChar);
    foreach(string part in path.Substring(3).Split(Path.DirectorySeparatorChar))if(part.Length==0||part=="."||part==".."||part.IndexOfAny(Path.GetInvalidFileNameChars())>=0||part.Contains("#")||part.EndsWith(".")||part.EndsWith(" "))return false;
    file=Path.GetFullPath(path);return true;
   }catch{return false;}
  }
  public static bool Supported(string value){string file;int line;if(TryLocal(value,out file,out line))return true;Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri)&&(uri.Scheme=="https"||uri.Scheme=="http")&&uri.UserInfo.Length==0;}
  public static List<MessagePart> Parse(string text){
   text=text??"";var parts=new List<MessagePart>();int offset=0;
   foreach(Match match in Links.Matches(text)){
    string target=match.Groups["target"].Success?match.Groups["target"].Value.Trim().Trim('<','>'):match.Groups["bare"].Value.TrimEnd('.',',',';','!','?','。','，','；');
    if(!Supported(target))continue;
    if(match.Index>offset)parts.Add(new MessagePart{Text=text.Substring(offset,match.Index-offset)});
    bool markdown=match.Groups["target"].Success;int length=markdown?match.Length:target.Length;
    parts.Add(new MessagePart{Text=markdown?match.Groups["label"].Value:target,Target=target});offset=match.Index+length;
   }
   if(offset<text.Length)parts.Add(new MessagePart{Text=text.Substring(offset)});return parts;
  }
  public static ProcessStartInfo OpenInfo(string target,LocalRepositorySettings settings){
   string file;int line;if(TryLocal(target,out file,out line)){
    if(!File.Exists(file))throw new FileNotFoundException("本地文件不存在，请核对代码路径："+file);
    string editor=string.IsNullOrWhiteSpace(settings.Editor)?LocalRepository.FindEditor():settings.Editor;
    if(string.IsNullOrWhiteSpace(editor))throw new InvalidOperationException("未找到编辑器，请在图谱的「配置本地仓库」中选择 VS Code 或 Cursor。");
    return LocalRepository.OpenInfo(editor,new LocalCodeTarget{File=file,Line=line});
   }
   if(!Supported(target))throw new InvalidDataException("不支持这个链接地址。");
   return new ProcessStartInfo(new Uri(target).AbsoluteUri){UseShellExecute=true};
  }
 }
 // Shared by normal chat, floating chat and graph consultation. Never interpret message text as XAML.
 public sealed class MessageText : RichTextBox {
  public readonly string OriginalText;
  public MessageText(string text,Brush color,double size,Action<string> open=null){
   OriginalText=text??"";IsReadOnly=true;IsDocumentEnabled=true;IsReadOnlyCaretVisible=false;IsUndoEnabled=false;
   Background=Brushes.Transparent;Foreground=color;BorderThickness=new Thickness(0);Padding=new Thickness(0);FontFamily=Theme.ReadingFont;FontSize=size;
   HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;
   SelectionBrush=Theme.Brush("#88E5B2C5");IsInactiveSelectionHighlightEnabled=true;FocusVisualStyle=null;
   var paragraph=new Paragraph{Margin=new Thickness(0),LineHeight=size*1.65};
   foreach(var part in MessageLinks.Parse(OriginalText)){
    if(part.Target==null){paragraph.Inlines.Add(new Run(part.Text));continue;}
    string target=part.Target;var link=new Hyperlink(new Run(part.Text)){Foreground=Theme.Brush("#2168AD"),ToolTip=target,Cursor=Cursors.Hand};
    link.Click+=delegate{if(open!=null){open(target);return;}try{Process.Start(MessageLinks.OpenInfo(target,LocalRepository.Load()));}catch(Exception e){PopupDialog.Show(Window.GetWindow(this),e.Message,"无法打开代码链接",MessageBoxButton.OK,MessageBoxImage.Information);}};
    paragraph.Inlines.Add(link);
   }
   Document=new FlowDocument(paragraph){PagePadding=new Thickness(0),FontFamily=Theme.ReadingFont,FontSize=size,Foreground=color};
   var menu=MenuTheme.Create();var copy=MenuTheme.Item("复制选中","\uE8C8",null);copy.Click+=delegate{ChatView.TryCopy(Selection.Text);};
   var all=MenuTheme.Item("复制整条","\uE8A5",null);all.Click+=delegate{ChatView.TryCopy(OriginalText);};
   var select=MenuTheme.Item("全选","\uE8B3",null);select.Click+=delegate{Focus();SelectAll();};menu.Opened+=delegate{copy.IsEnabled=!Selection.IsEmpty;};
   menu.Items.Add(copy);menu.Items.Add(all);menu.Items.Add(new Separator());menu.Items.Add(select);ContextMenu=menu;
   TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
  }
  public static Button CopyButton(string text,Func<string,bool> copy=null){copy=copy??ChatView.TryCopy;Button button=null;button=Ui.Button("复制整条",delegate{button.Content=copy(text)?"已复制":"复制失败，请重试";},false);button.FontSize=12;button.Padding=new Thickness(8,3,8,3);button.Margin=new Thickness(0,4,8,0);button.ToolTip="复制完整原文，保留代码路径与链接";return button;}
 }
}
