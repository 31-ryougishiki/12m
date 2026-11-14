using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
namespace ElymsPet {
 public sealed class ArchivePanel {
  readonly PetWindow pet;readonly ArchiveService archive;readonly Action<PanelTab> selectTab;
  readonly TabControl pages=new TabControl{Background=Theme.Canvas,BorderThickness=new Thickness(0)};
  readonly StackPanel candidates=new StackPanel(),workflows=new StackPanel();
  readonly TextBlock current=Ui.Text("",13,"#78616A");
  readonly TextBox topic=Ui.Input("",44),flowName=Ui.Input("",44),name=Ui.Input("",44);
  readonly ComboBox slots=new ComboBox{MinWidth=180,MinHeight=36,Margin=new Thickness(0,0,8,8),DisplayMemberPath=""};
  sealed class Conversation {
   public TextBlock Question=Ui.Text("",16,"#4F3B43"),Progress=Ui.Text("",14,"#78616A"),Status=Ui.Text("",13,"#447969");
   public TextBox Answer=Ui.Input("",130),History=Ui.Input("",240);public Button Send,Resume;public StackPanel Root;
  }
  Conversation chat,review;Button skip,finish,match,newTopic;
  string visibleSlot="";bool refreshing;int profileRevision=-1,slotRevision=-1,turnRevision=-1;
  public UIElement Root {get;private set;}
  public ArchivePanel(PetWindow pet,Action<PanelTab> selectTab){this.pet=pet;archive=pet.Archive;this.selectTab=selectTab;Root=Build();archive.Changed+=Refresh;Refresh();pages.SelectedIndex=archive.ReviewingWorkflow?1:0;}
  Conversation BuildConversation(bool isReview){
   var c=new Conversation();c.Answer.AcceptsReturn=true;c.Answer.VerticalContentAlignment=VerticalAlignment.Top;c.Answer.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;c.Answer.MaxLength=2000;c.Answer.FontSize=15;c.Answer.ToolTip="Enter 发送，Shift + Enter 换行";
   c.Answer.PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter&&(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Shift)==0){Send(c,isReview);e.Handled=true;}};
   c.History.IsReadOnly=true;c.History.AcceptsReturn=true;c.History.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
   c.Send=Ui.Button(isReview?"保存补充并继续":"发送回答",delegate{Send(c,isReview);},true);
   c.Resume=Ui.Button("继续 / 重试",delegate{if(archive.ReviewingWorkflow==isReview)archive.ResumeInterview();},false);
   var actions=Ui.Row(c.Send,c.Resume);
   if(isReview){skip=Ui.Button("没有补充，下一项",archive.SkipWorkflowUnit,false);finish=Ui.Button("结束本轮补充",archive.EndWorkflowReview,false);actions.Children.Add(skip);actions.Children.Add(finish);}
   c.Root=Ui.Stack(c.Progress,new ScrollViewer{Content=c.Question,MaxHeight=280,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},c.Answer,actions,c.Status,new Expander{Header="存档交流记录",Content=c.History});return c;
  }
  UIElement Build(){
   var style=new Style(typeof(TabItem));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(15,10,15,10)));style.Setters.Add(new Setter(Control.FontSizeProperty,14.0));style.Setters.Add(new Setter(Control.TemplateProperty,System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TabItem'><Border x:Name='Box' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='#FFFFFF' BorderBrush='#8996B5' BorderThickness='2' Margin='0,0,4,4' Padding='{TemplateBinding Padding}'><ContentPresenter ContentSource='Header'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='Box' Property='Background' Value='#DFF7F2'/><Setter TargetName='Box' Property='BorderBrush' Value='#008D83'/></Trigger></ControlTemplate.Triggers></ControlTemplate>")));pages.ItemContainerStyle=style;
   topic.MaxLength=flowName.MaxLength=100;topic.ToolTip="填写想讨论的新主题";flowName.ToolTip="填写大概流程名，例如：拉起模型测试";
   chat=BuildConversation(false);review=BuildConversation(true);
   newTopic=Ui.Button("开始交流",StartTopic,true);match=Ui.Button("匹配流程，逐项补充",Match,true);
   pages.Items.Add(new TabItem{Header="交流新内容",Content=Ui.Card(Ui.Stack(Ui.Text("今天想聊点什么？",22,"#4F3B43"),Ui.Text("从一个主题或经验开始，曳尔沐会通过问答帮你整理知识和新流程。",14,"#78616A"),topic,Ui.Row(newTopic),chat.Root))});
   pages.Items.Add(new TabItem{Header="补充已有流程",Content=Ui.Card(Ui.Stack(Ui.Text("让已有流程更完整",22,"#4F3B43"),Ui.Text("填大概名称，匹配已有流程，再逐项补充前置条件、步骤与验收标准。",14,"#78616A"),flowName,Ui.Row(match),candidates,review.Root))});
   pages.Items.Add(new TabItem{Header="流程",Content=Ui.Card(Ui.Stack(Ui.Text("我的流程",20,"#4F3B43"),Ui.Text("选择一套流程逐项补充，核实后确认使用。",13,"#78616A"),workflows))});

   pages.Items.Add(new TabItem{Header="云同步",Content=Ui.Button("打开云同步插件",delegate{selectTab(PanelTab.Cloud);},false)});
   name.MaxLength=24;name.Width=200;name.ToolTip="新建或重命名存档的名称";
   slots.SelectionChanged+=delegate{if(!refreshing&&slots.SelectedItem is ArchiveSlot){candidates.Children.Clear();archive.Select((ArchiveSlot)slots.SelectedItem);}};
   var manage=Ui.Stack(Ui.Row(slots),Ui.Row(name,Ui.Button("新建",delegate{archive.Create(name.Text);name.Clear();},false),Ui.Button("重命名",delegate{archive.Rename(name.Text);name.Clear();},false)),Ui.Button("AI 连接设置",delegate{selectTab(PanelTab.Settings);},false));
   return Ui.Stack(current,pages,new Expander{Header="切换 / 管理存档",Content=manage,Margin=new Thickness(0,12,0,0)});
  }
  public void StartGraphTopic(string value){pages.SelectedIndex=0;topic.Text=value;StartTopic();}
  void StartTopic(){if(archive.Busy||string.IsNullOrWhiteSpace(topic.Text))return;if(archive.ReviewingWorkflow){archive.Notify("请先在「补充已有流程」结束本轮，再开始新话题。");return;}archive.Focus(topic.Text.Trim());}
  void Match(){if(archive.Busy)return;candidates.Children.Clear();archive.MatchWorkflow(flowName.Text,delegate(List<ArchiveWorkflow> found){if(found.Count==1){Begin(found[0]);return;}foreach(var item in found){var w=item;candidates.Children.Add(Ui.Button("补充「"+w.Title+"」",delegate{Begin(w);},false));}});}
  void Begin(ArchiveWorkflow workflow){if(archive.StartWorkflowReview(workflow)){candidates.Children.Clear();pages.SelectedIndex=1;review.Answer.Clear();}}
  void Send(Conversation c,bool isReview){if(archive.Busy||archive.ReviewingWorkflow!=isReview)return;if(archive.Answer(c.Answer.Text))c.Answer.Clear();}
  void Refresh(){
   if(visibleSlot!=archive.Slot.Id){visibleSlot=archive.Slot.Id;chat.Answer.Clear();review.Answer.Clear();candidates.Children.Clear();}
   bool busy=archive.Busy,isReview=archive.ReviewingWorkflow;current.Text=archive.Slot.Name;
   foreach(var c in new[]{chat,review}){bool active=(c==review)==isReview;
    c.Status.Text=archive.Status;c.Progress.Text=c==review?(isReview?archive.ReviewProgress:"选择流程后，逐项询问需要补充的内容。"):"深入访谈 · 新知识与新流程";
    c.Question.Text=active?archive.LatestQuestion:"";
    if(c.Question.Text.Length==0)c.Question.Text=c==review?"填写流程名称，或从「流程」页选择。":isReview?"流程补充正在进行，可切换到「补充已有流程」继续。":"填写新主题，开始交流。";
    c.Answer.IsEnabled=c.Send.IsEnabled=active&&!busy&&archive.HasQuestion&&!archive.Slot.Paused;
    c.Resume.IsEnabled=active&&!busy;c.Resume.Visibility=active&&(archive.ReviewWaiting||archive.Slot.Paused||!archive.HasQuestion)?Visibility.Visible:Visibility.Collapsed;
   }
   skip.Visibility=finish.Visibility=isReview?Visibility.Visible:Visibility.Collapsed;skip.IsEnabled=!busy&&!archive.ReviewWaiting;finish.IsEnabled=!busy;
   match.IsEnabled=!busy&&!archive.ReviewWaiting;newTopic.IsEnabled=!busy&&!isReview;slots.IsEnabled=!busy;
   if(turnRevision!=archive.TurnRevision||slotRevision!=archive.SlotRevision){var text=new System.Text.StringBuilder();var turns=archive.Slot.Turns;for(int i=Math.Max(0,turns.Count-40);i<turns.Count;i++)text.Append(turns[i].Role=="ask"?"曳尔沐：":"你：").Append(turns[i].Text).Append("\n\n");chat.History.Text=review.History.Text=text.ToString();turnRevision=archive.TurnRevision;}
   if(profileRevision!=archive.ProfileRevision||slotRevision!=archive.SlotRevision){
    workflows.Children.Clear();if(archive.Slot.Workflows.Count==0)workflows.Children.Add(Ui.Text("还没有流程。先在「交流新内容」聊聊你的操作步骤。",14,"#78616A"));
    foreach(var item in archive.Slot.Workflows){var w=item;workflows.Children.Add(Ui.Card(Ui.Stack(Ui.Text(w.Title,18,"#4F3B43"),Ui.Text(w.Confirmed?"已确认":"草稿 · 待核实",12,"#78616A"),Ui.Text(ArchiveKnowledge.WorkflowText(w),14,"#59434B"),Ui.Row(Ui.Button("逐项补充",delegate{Begin(w);},true),Ui.Button(w.Confirmed?"退回草稿":"确认流程",delegate{archive.ConfirmWorkflow(w);},false)))));}
    refreshing=true;slots.ItemsSource=null;slots.ItemsSource=archive.Book.Slots;slots.SelectedItem=archive.Slot;refreshing=false;profileRevision=archive.ProfileRevision;slotRevision=archive.SlotRevision;
   }
  }
  public void Save(){archive.Save();}
  public void Dispose(){archive.Changed-=Refresh;}
 }
}



