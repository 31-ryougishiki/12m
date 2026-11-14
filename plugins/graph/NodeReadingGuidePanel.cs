using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel {
  int guideTab=1;
  bool ReadingGuidePanel(StackPanel panel,DomainNode node){
   var guide=node.ReadingGuide;if(guide==null||NodeReadingGuide.Validate(guide).Length>0)return false;
   bool current=guide.SourceRevision==NodeReadingGuide.Revision(node);
   panel.Children.Add(Text(current?"曳尔沐的学习辅助 · 原介绍保留":"原文已变化 · 以下翻译可能过期，请重新翻译",12,current?"#247A7B":"#A56700"));
   var original=new StackPanel();ApproachPanel(original,node.Approach);
   var terms=new StackPanel();foreach(var item in guide.Terms){var box=new StackPanel();box.Children.Add(Text(item.Term,16,"#247A7B"));box.Children.Add(ReadableText(item.Meaning));terms.Children.Add(new Border{Child=box,Background=Theme.Brush("#F5F8FC"),Padding=new Thickness(10),Margin=new Thickness(0,0,0,8)});}
   var steps=new StackPanel();int index=0;foreach(var item in guide.Steps){var step=item;var box=new StackPanel();box.Children.Add(Text("步骤 "+(++index)+" · 介绍中的原话",12,"#49788F"));box.Children.Add(ReadableText(step.Original));box.Children.Add(Text("对应代码做什么",12,"#49788F"));box.Children.Add(ReadableText(step.Explanation));
    if(string.IsNullOrEmpty(step.CodeUrl))box.Children.Add(Text("对应源码尚未定位",12,"#A56700"));else{var notice=Text("",12,"#687794");box.Children.Add(Link("本地代码 · "+RepositoryLearning.CodeLocationLabel(step.CodeUrl),delegate{OpenLocalCode(step.CodeUrl,notice);},step.CodeUrl));box.Children.Add(SourceLink(step.CodeUrl,"查看固定版本源码 ↗"));box.Children.Add(notice);}
    steps.Children.Add(new Border{Child=box,Background=Theme.Brush("#F5F8FC"),Padding=new Thickness(10),Margin=new Thickness(0,0,0,10)});
   }
   var tabs=new TabControl{Background=Brushes.Transparent,BorderThickness=new Thickness(0),Margin=new Thickness(0,4,0,12)};string[] labels={"实现原文","术语翻译","流程对照"};UIElement[] contents={original,terms,steps};for(int i=0;i<3;i++)tabs.Items.Add(new TabItem{Header=labels[i],FontFamily=Theme.ReadingFont,FontSize=13,Padding=new Thickness(7,6,7,6),Content=new Border{Padding=new Thickness(0,10,0,0),Child=contents[i]}});tabs.SelectedIndex=guideTab;tabs.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs e){if(e.Source==tabs)guideTab=tabs.SelectedIndex;};panel.Children.Add(tabs);panel.Children.Add(Text("AI 翻译与流程解释需结合源码核对；未定位的步骤不会补造跳转链接。",12,"#687794"));return true;
  }
 }
}
