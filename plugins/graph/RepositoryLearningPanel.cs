using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel {
  int detailTab;
  TextBox reflectionEditor;string reflectionKey="";readonly System.Collections.Generic.Dictionary<string,string> reflectionDrafts=new System.Collections.Generic.Dictionary<string,string>();
  void RememberReflection(){if(reflectionEditor!=null&&reflectionKey.Length>0)reflectionDrafts[reflectionKey]=reflectionEditor.Text;reflectionEditor=null;reflectionKey="";}

  TextBlock Link(string label,Action action,string hint=null){
   var text=Text("",14,"#5368B2");var link=new Hyperlink(new Run(label)){Foreground=Theme.Brush("#5368B2"),ToolTip=hint};link.Click+=delegate{action();};text.Inlines.Add(link);if(RepositoryLearning.SafeUrl(hint)){var copy=new MenuItem{Header="复制链接地址"};copy.Click+=delegate{GraphCopyText.Copy(hint);};text.ContextMenu.Items.Add(copy);}return text;
  }
  TextBlock SourceLink(string url,string label=null){return Link(label??url,delegate{OpenLearningUrl(url);},url);}
  static void DetailSection(StackPanel panel,string title,string value){panel.Children.Add(Text(title,12,"#49788F"));panel.Children.Add(Text(string.IsNullOrWhiteSpace(value)?"尚未补充":value,15,"#33405B"));}
  void NodeDetails(DomainNode node){
   var learning=node.Learning??new NodeLearning();var locations=RepositoryLearning.CodeLocations(node);
   var quick=new WrapPanel{Margin=new Thickness(0,0,0,8)};
   var codeEntry=locations.Count>0?SourceLink(locations[0],"↗ 跳转代码 "+new Uri(locations[0]).Fragment):Link("＋ 设置代码位置",delegate{EditLearning(node);});codeEntry.Margin=new Thickness(0,0,20,0);quick.Children.Add(codeEntry);
   var localNotice=Text("",13,"#687794");localNotice.Visibility=Visibility.Collapsed;
   if(locations.Count>0){var local=Link("本地打开",delegate{OpenLocalCode(locations[0],localNotice);});local.Margin=new Thickness(0,0,16,0);quick.Children.Insert(0,local);}
   quick.Children.Add(Link("学习设置",delegate{EditLearning(node);}));inspector.Children.Add(quick);inspector.Children.Add(localNotice);
   var overview=new StackPanel();DetailSection(overview,"节点介绍",node.Summary);if(!ReadingGuidePanel(overview,node))ApproachPanel(overview,node.Approach);
   var relations=new StackPanel();int count=0;foreach(var edge in graph.Edges)if(edge.From==node.Id||edge.To==node.Id){var e=edge;string id=e.From==node.Id?e.To:e.From;var other=graph.Nodes.Find(delegate(DomainNode n){return n.Id==id;});if(other==null)continue;relations.Children.Add(Link((e.From==node.Id?e.Label+" → ":"← "+e.Label+" · ")+other.Title,delegate{SelectNode(id);CenterSelected();},e.Evidence));count++;}
   if(count>0)overview.Children.Add(new Expander{Header="关联节点 · "+count,Content=relations,IsExpanded=count<=3});
   overview.Children.Add(new Expander{Header="版本与核对记录",Content=Text(node.Status+"\n"+node.Version,12,"#687794"),IsExpanded=false});
   var editing=new Grid{Margin=new Thickness(0,12,0,0)};
   editing.ColumnDefinitions.Add(new ColumnDefinition());editing.ColumnDefinitions.Add(new ColumnDefinition());editing.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});editing.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
   var rewrite=Link("请曳尔沐翻译术语",delegate{if(archive.Busy){status.Text="请等待当前任务结束。";return;}guideTab=1;archive.TranslateNodeTerms(graph,node,delegate(GraphEditOutcome result){if(!disposed)status.Text=result.Message;});},"保留原介绍，另存术语翻译，并把介绍中的流程逐步对应到代码");rewrite.Margin=new Thickness(0,0,16,12);editing.Children.Add(rewrite);
   var edit=Link("请曳尔沐修改节点",delegate{RequestAiGraphEdit(node,null);},"修改已有内容，或委托新增节点与关联");edit.Margin=new Thickness(0,0,0,12);Grid.SetColumn(edit,1);editing.Children.Add(edit);
   var chat=Link("继续聊聊",delegate{interview(graph.Title+" / "+node.Title);});Grid.SetRow(chat,1);Grid.SetColumnSpan(chat,2);editing.Children.Add(chat);overview.Children.Add(editing);
   var code=new StackPanel();
   if(locations.Count==0){code.Children.Add(Text("尚无固定代码行链接。请在学习设置中粘贴 GitHub 的永久行链接；已有来源中的代码行会自动识别。",14,"#687794"));code.Children.Add(Link("设置代码位置",delegate{EditLearning(node);}));}
   else {code.Children.Add(Text("点击文件名在本地编辑器打开；网页链接保留固定版本定位。",13,"#687794"));foreach(string location in locations){string url=location;code.Children.Add(Link(RepositoryLearning.CodeLocationLabel(url),delegate{OpenLocalCode(url,localNotice);},url));var actions=new WrapPanel();var web=SourceLink(url,"网页源码 ↗");web.Margin=new Thickness(0,0,16,10);actions.Children.Add(web);actions.Children.Add(Link("配置本地仓库",delegate{ConfigureLocalRepository(url);}));code.Children.Add(actions);}}
   code.Children.Add(Text("节点代码片段",12,"#49788F"));
   if(string.IsNullOrWhiteSpace(node.Code))code.Children.Add(Text("尚未补充代码片段",14,"#687794"));else code.Children.Add(new TextBox{Text=node.Code,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,FontFamily=new FontFamily("Consolas"),FontSize=14,Background=Theme.Brush("#EDF4F8"),Foreground=Theme.Brush("#176459"),BorderThickness=new Thickness(0),Padding=new Thickness(10),HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=350});
   var resources=ResourcesPanel(node);
   var tabs=new TabControl{BorderThickness=new Thickness(0),Background=Brushes.Transparent,Margin=new Thickness(0,4,0,0)};
   string[] names={"概览","代码","学习","资料"};UIElement[] pages={overview,code,LearningPanel(node),resources};
   for(int i=0;i<names.Length;i++)tabs.Items.Add(new TabItem{Header=names[i],FontFamily=Theme.ReadingFont,FontSize=14,Padding=new Thickness(9,7,9,7),Content=new Border{Padding=new Thickness(0,14,0,0),Child=pages[i]}});
   tabs.SelectedIndex=detailTab;tabs.SelectionChanged+=delegate(object sender,SelectionChangedEventArgs e){if(e.Source==tabs)detailTab=tabs.SelectedIndex;};inspector.Children.Add(tabs);
  }
  StackPanel LearningPanel(DomainNode node){
   var learning=node.Learning??new NodeLearning();var box=new StackPanel();
   DetailSection(box,"这一步的学习目标",string.IsNullOrWhiteSpace(learning.Goal)?"先定位输入与输出，再沿关联节点追踪一次请求。":learning.Goal);
   var steps=new WrapPanel();foreach(var edge in graph.Edges)if(edge.Label=="学习下一步"&&(edge.From==node.Id||edge.To==node.Id)){string target=edge.From==node.Id?edge.To:edge.From;var next=graph.Nodes.Find(delegate(DomainNode n){return n.Id==target;});if(next!=null){var step=Link(edge.From==node.Id?"下一步 →":"← 上一步",delegate{SelectNode(target);CenterSelected();},next.Title);step.Margin=new Thickness(0,0,18,8);steps.Children.Add(step);}}box.Children.Add(steps);
   var output=Text("",14,"#33405B");output.Visibility=Visibility.Collapsed;
   var explain=Button("曳尔沐讲解",delegate{});explain.HorizontalAlignment=HorizontalAlignment.Left;
   var feedback=EditAction("检查复述",delegate{});var feedbackOutput=Text("",14,"#33405B");feedbackOutput.Visibility=Visibility.Collapsed;
   explain.Click+=delegate{explain.IsEnabled=false;feedback.IsEnabled=false;output.Visibility=Visibility.Visible;output.Text="正在组织讲解…";archive.ExplainRepositoryNode(graph,node,"解释当前代码及整体流程中的位置；必要时补充特性来源、设计模式、数学或语言基础，并注明证据缺口",delegate(string answer){if(disposed)return;output.Text=answer;explain.IsEnabled=true;feedback.IsEnabled=true;});};box.Children.Add(explain);box.Children.Add(output);
   var practice=new StackPanel{Margin=new Thickness(0,10,0,0)};practice.Children.Add(Text(string.IsNullOrWhiteSpace(learning.Question)?"不看解释，说出这一步由谁调用、改变了什么、失败会影响哪里？":learning.Question,14,"#33405B"));
   string draftKey=archive.Slot.Id+"/"+graph.Id+"/"+node.Id,draftText;var reflection=Ui.Input(reflectionDrafts.TryGetValue(draftKey,out draftText)?draftText:learning.Reflection??"",110);reflectionEditor=reflection;reflectionKey=draftKey;reflection.AcceptsReturn=true;reflection.TextWrapping=TextWrapping.Wrap;reflection.MaxLength=12000;reflection.ToolTip="写下自己的解释，保存或请求反馈";practice.Children.Add(reflection);
   var save=EditAction("保存复述",delegate{SaveLearning(node,delegate(NodeLearning value){value.Reflection=reflection.Text;});});practice.Children.Add(Ui.Row(save,feedback));
   feedback.Click+=delegate{feedbackOutput.Visibility=Visibility.Visible;if(string.IsNullOrWhiteSpace(reflection.Text)){feedbackOutput.Text="先写下自己的理解，再请求反馈。";return;}var copy=DomainKnowledge.Clone(graph);var draft=copy.Nodes.Find(delegate(DomainNode n){return n.Id==node.Id;});if(draft.Learning==null)draft.Learning=new NodeLearning();draft.Learning.Reflection=reflection.Text;explain.IsEnabled=false;feedback.IsEnabled=false;feedbackOutput.Text="正在检查复述…";archive.ExplainRepositoryNode(copy,draft,"对我的复述指出正确处、证据不足处和下一步验证动作；不要给掌握分数",delegate(string answer){if(disposed)return;feedbackOutput.Text=answer;explain.IsEnabled=true;feedback.IsEnabled=true;});};practice.Children.Add(feedbackOutput);
   box.Children.Add(new Expander{Header="自测 · 用自己的话解释",Content=practice,IsExpanded=false});return box;
  }
  StackPanel ResourcesPanel(DomainNode node){
   var box=new StackPanel();var urls=RepositoryLearning.SourceUrls(node.Sources);box.Children.Add(Text("节点来源",12,"#49788F"));foreach(string url in urls)box.Children.Add(SourceLink(url,RepositoryLearning.PinnedCode(url)?RepositoryLearning.CodeLocationLabel(url):url));
   if(urls.Count==0)box.Children.Add(Text(string.IsNullOrWhiteSpace(node.Sources)?"尚未补充来源":node.Sources,14,"#687794"));else box.Children.Add(new Expander{Header="原始来源说明",Content=Text(node.Sources,13,"#687794"),IsExpanded=false});
   var selectedEvidence=RepositoryLearning.SelectedEvidence(node);foreach(var e in selectedEvidence){DetailSection(box,e.Kind+" · "+e.Title,e.Reason);box.Children.Add(SourceLink(e.Url,"查看已人工核对的出处 ↗"));}
   var candidates=new StackPanel();var learning=node.Learning;if(learning!=null&&learning.Evidence!=null)foreach(var evidence in learning.Evidence){if(evidence==null||selectedEvidence.Contains(evidence))continue;var e=evidence;string reason=RepositoryLearning.HiddenReason(e,node);candidates.Children.Add(Text(e.Title+" · "+(reason.Length==0?"超过默认三条上限":reason),13,"#687794"));candidates.Children.Add(Text(e.Reason+"\n依据："+e.Excerpt,13,"#33405B"));if(RepositoryLearning.SafeUrl(e.Url))candidates.Children.Add(SourceLink(e.Url,"打开原文核对 ↗"));if(!e.Reviewed)candidates.Children.Add(EditAction("标记为已人工核对",delegate{int index=learning.Evidence.IndexOf(e);SaveLearning(node,delegate(NodeLearning value){value.Evidence[index].Reviewed=true;});}));}
   if(candidates.Children.Count>0)box.Children.Add(new Expander{Header="候选资料与筛选原因",Content=candidates,IsExpanded=false});box.Children.Add(Text("人工核对不等于自动验证。",12,"#687794"));return box;
  }
  void OpenLearningUrl(string url){if(!RepositoryLearning.SafeUrl(url))return;try{System.Diagnostics.Process.Start(url);}catch(Exception ex){status.Text=ex.Message;}}
  bool SaveLearning(DomainNode node,Action<NodeLearning> change){
   if(graph==null||!graph.Nodes.Contains(node)){status.Text="图谱已变化，请重新选择节点再保存。";return false;}
   var current=DomainKnowledge.List(archive.Slot).Find(delegate(DomainGraph g){return g.Id==graph.Id;});
   if(current==null||GraphEditing.Revision(current)!=GraphEditing.Revision(graph)){status.Text="图谱已变化，请重新选择节点再保存。";return false;}
   var copy=DomainKnowledge.Clone(graph);var target=copy.Nodes.Find(delegate(DomainNode n){return n.Id==node.Id;});if(target.Learning==null)target.Learning=new NodeLearning();change(target.Learning);string error=RepositoryLearning.Validate(target.Learning);if(error.Length>0){status.Text=error;return false;}return archive.SaveDomainGraph(copy);
  }
  void EditLearning(DomainNode node){
   var learning=node.Learning??new NodeLearning();var locations=RepositoryLearning.CodeLocations(node);var url=Ui.Input(locations.Count>0?locations[0]:"",60);var goal=Ui.Input(learning.Goal??"",80);var question=Ui.Input(learning.Question??"",80);foreach(var input in new[]{url,goal,question}){input.MaxLength=12000;input.TextWrapping=TextWrapping.Wrap;}
   var dialog=new Window{Owner=LearningOwner,Title="源码学习设置",Width=620,Height=560,MaxHeight=SystemParameters.WorkArea.Height-40,Background=Theme.Canvas,WindowStartupLocation=WindowStartupLocation.CenterOwner};
   var error=Text("",13,"#AD506B");dialog.Content=new ScrollViewer{Content=new Border{Padding=new Thickness(24),Child=Ui.Stack(Text("这一步要学会什么",22,"#28324A"),goal,Text("源码位置：粘贴 GitHub 固定提交的行链接",14,"#33405B"),url,Text("在 GitHub 文件页选中行号，在行号菜单选择 Copy permalink。保存后，点击标题下的「跳转代码」。当前打开浏览器中的源码，不打开本地编辑器。",13,"#687794"),Text("用自己的话回答的问题",14,"#33405B"),question,error,Button("保存学习设置",delegate{if(SaveLearning(node,delegate(NodeLearning v){v.CodeUrl=url.Text.Trim();v.Goal=goal.Text;v.Question=question.Text;}))dialog.Close();else error.Text=status.Text;}))}};GraphCopyText.Enable(dialog);dialog.ShowDialog();
  }
 }
}
