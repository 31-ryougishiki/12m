using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel {
  readonly Grid studyTools=new Grid{Margin=new Thickness(0,0,0,12)};
  readonly TextBox consultInput=Ui.Input("",64);
  readonly TextBlock consultScope=Text("",12,"#52617F");
  readonly StackPanel consultLines=new StackPanel();
  ChatService consultation;Button consultSend;Expander consultReplies;Border consultCard;ScrollViewer consultScroll;
  void InitializeStudyTools(ChatService chat){
   consultation=chat;
   {var box=consultInput;box.Background=Theme.Brush("#FFFFFF");box.Foreground=Theme.Brush("#29324B");box.FontSize=14;box.AcceptsReturn=true;box.VerticalContentAlignment=VerticalAlignment.Top;box.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;box.Margin=new Thickness(0);box.ContextMenu=MenuTheme.EditMenu(box);}
   consultInput.Tag="graph-consult-input";consultInput.MaxLength=4000;consultInput.ToolTip="询问当前页面、模块或节点；Enter 发送，Shift+Enter 换行";
   consultSend=EditAction("问曳尔沐",SendConsultation);
   var inputRow=new DockPanel();DockPanel.SetDock(consultSend,Dock.Right);inputRow.Children.Add(consultSend);inputRow.Children.Add(consultInput);
   consultScroll=new ScrollViewer{Content=consultLines,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
   consultReplies=new Expander{Header="咨询记录 · 与陪伴对话同步",Content=consultScroll,Foreground=Theme.Brush("#5368B2"),FontFamily=Theme.ReadingFont,FontSize=13,Margin=new Thickness(0,6,0,0)};
   var consultationBody=Ui.Stack(consultScope,inputRow,consultReplies);
   consultCard=StudyCard(consultationBody);
   studyTools.Children.Add(consultCard);
   ChatInput.EnterSends(consultInput,SendConsultation);
   if(chat!=null){var lines=chat.Lines;for(int i=Math.Max(0,lines.Length-12);i<lines.Length;i++)ConsultationLine(lines[i]);chat.LineAdded+=ConsultationLine;chat.Changed+=ConsultationChanged;chat.Cleared+=ClearConsultation;}
   ConsultationChanged();
  }
  static Border StudyCard(UIElement child){return new Border{Child=child,Background=Theme.Brush("#F8FAFF"),BorderBrush=Theme.Brush("#CCD5E5"),BorderThickness=new Thickness(1),Padding=new Thickness(12),VerticalAlignment=VerticalAlignment.Top};}
  void SendConsultation(){if(consultation==null||consultation.Busy||string.IsNullOrWhiteSpace(consultInput.Text))return;consultReplies.IsExpanded=true;string question=consultInput.Text;consultInput.Clear();consultation.Send(question);}
  void ConsultationLine(ChatLine line){
   if(disposed)return;bool mine=line.Speaker==ChatSpeaker.User,notice=line.Speaker==ChatSpeaker.Notice;
   var color=notice?Theme.TextDim:mine?Theme.UserText:Theme.HerText;
   var label=new TextBlock{Text=mine?"你：":notice?"提示：":"曳尔沐：",Foreground=color,FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)};
   var body=new TextBlock{Text=line.Text,Foreground=color,FontFamily=Theme.ReadingFont,FontSize=14,TextWrapping=TextWrapping.Wrap,LineHeight=23};
   consultLines.Children.Add(new Border{Child=Ui.Stack(label,body),Background=notice?Theme.Brush("#F0F2F5"):mine?Theme.UserBubble:Theme.HerBubble,BorderBrush=notice?Theme.Brush("#CCD5E5"):mine?Theme.UserBorder:Theme.HerBorder,BorderThickness=new Thickness(1),Padding=new Thickness(10,8,10,8),Margin=new Thickness(mine?36:0,0,mine||notice?0:36,10)});
   while(consultLines.Children.Count>60)consultLines.Children.RemoveAt(0);consultScroll.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(delegate{if(!disposed)consultScroll.ScrollToEnd();}));
  }
  void ConsultationChanged(){if(disposed)return;consultSend.IsEnabled=consultation!=null&&!consultation.Busy;consultSend.Content=consultation!=null&&consultation.Busy?"正在回复…":"问曳尔沐";consultInput.IsEnabled=consultation!=null;}
  void ClearConsultation(){consultLines.Children.Clear();}
  void UpdateStudyScope(){
   if(consultCard==null)return;
   var module=SelectedModule();if(module==null&&architecture!=null){module=architecture.Modules.Find(m=>m.Id==architectureModule);if(module==null)module=RepositoryArchitecture.Parent(architecture,selected);}
   string scope=graph==null?"未选择专题":module==null?graph.Title+" / 总览":module.Repository+" / "+RepositoryArchitecture.PathLabel(architecture,module.Id);
   var node=Selected();consultScope.Text="本页咨询 · "+(node==null?scope:node.Title);consultScope.ToolTip="发送时同步当前图谱视角、节点资料和可读取的源码上下文";
  }
  void DisposeStudyTools(){if(consultation!=null){consultation.LineAdded-=ConsultationLine;consultation.Changed-=ConsultationChanged;consultation.Cleared-=ClearConsultation;}}
 }
}
