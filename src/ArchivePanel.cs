using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ElymsPet {
 /// <summary>「存档单元」页：存档管理、访谈整理与个人画像；从陪伴面板里独立出来，单独维护。</summary>
 public sealed class ArchivePanel {
  readonly PetWindow pet;
  readonly Action<PanelTab> selectTab;
  readonly ArchiveService archive;
  KnowledgeGraphPanel graphPanel;
  WrapPanel archiveChips;
  TextBox archiveName,interviewLog,interviewInput,focusInput,knowledgeSearch;
  StackPanel resourceList,workflowList,episodeList,changeList;
  TextBlock focusStatus;
  TextBlock archiveStatus,archiveNote,extractStatus,profileSummary,profileStats,interviewQuestion;
  StackPanel profileList;
  Button interviewSend,interviewAsk,extractButton;
  int seenTurns=-1,seenProfile=-1,seenSlots=-1;
  bool busyShown;
  public UIElement Root {get; private set;}
  public ArchivePanel(PetWindow target,Action<PanelTab> selectTab) {
   pet=target;this.selectTab=selectTab;archive=pet.Archive;
   Root=Build();
   archive.Changed+=OnArchiveChanged;
   RefreshAll();
  }
  Window Owner {get {return pet.Panel;}}
  MessageBoxResult Ask(string text,string title) {
   return Owner==null?PopupDialog.Show(text,title):PopupDialog.Show(Owner,text,title);
  }
  MessageBoxResult Ask(string text,string title,MessageBoxButton buttons,MessageBoxImage icon) {
   return Owner==null?PopupDialog.Show(text,title,buttons,icon):PopupDialog.Show(Owner,text,title,buttons,icon);
  }
  /// <summary>构建页面内容；由陪伴面板放进「存档单元」标签页。</summary>
  UIElement Build() {
   var tab=new StackPanel();
   var pages=new TabControl{Margin=new Thickness(0,10,0,0),Background=Theme.Canvas,BorderThickness=new Thickness(0)};
   var tabStyle=new Style(typeof(TabItem));
   tabStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(16,10,16,10)));
   tabStyle.Setters.Add(new Setter(Control.ForegroundProperty,Theme.TextMain));
   tabStyle.Setters.Add(new Setter(Control.BackgroundProperty,Theme.Sidebar));
   tabStyle.Setters.Add(new Setter(Control.FontSizeProperty,13.0));
   pages.ItemContainerStyle=tabStyle;
   archiveChips=new WrapPanel{Margin=new Thickness(0,2,0,4)};
   archiveName=Ui.Input("",40);archiveName.Width=200;archiveName.MaxLength=24;archiveName.Margin=new Thickness(0,0,9,9);
   archiveName.ToolTip="写新存档的名字；留空会自动命名为「存档 2」";
   archiveStatus=Ui.Text("",12,"#78616A");
   archiveNote=Ui.Text("",11,"#795F69");
   var head=Ui.Stack(
    Ui.Text("存档管理",18,"#4F3B43"),
    Ui.Text("每个存档独立保存工作背景、知识边界、资料和流程。访谈需要配置 API；断线时保留回答。",12,"#78616A"),
    Ui.Text("选择存档",12,"#78616A"),
    archiveChips,
    Ui.Text("新建 / 重命名用的名字（留空时自动命名）",11,"#795F69"),
    Ui.Row(archiveName,Ui.Button("新建存档",NewArchive,true),Ui.Button("重命名",RenameArchive,false),Ui.Button("删除存档",DeleteArchive,false)),
    Ui.Row(Ui.Button("导出 Markdown",ExportArchive,false),Ui.Button("打开数据文件夹",delegate {Directory.CreateDirectory(ArchiveStore.Folder);Process.Start("explorer.exe",ArchiveStore.Folder);},false),Ui.Button("接口设置",delegate {selectTab(PanelTab.Settings);},false)),
    archiveStatus,archiveNote);
   tab.Children.Add(Ui.Text("工作知识库",22,"#4F3B43"));
   tab.Children.Add(Ui.Text("先建立工作全貌，再一次聊透一个主题。随时纠正、跳过或暂停，下次接着聊。",12,"#78616A"));
   focusStatus=Ui.Text("",13,"#447969");tab.Children.Add(focusStatus);
   tab.Children.Add(pages);
   interviewLog=Ui.Input("",130);interviewLog.IsReadOnly=true;interviewLog.AcceptsReturn=true;interviewLog.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;interviewLog.VerticalContentAlignment=VerticalAlignment.Top;interviewLog.FontSize=13;interviewLog.Margin=new Thickness(0,0,0,4);
   interviewLog.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   interviewInput=Ui.Input("",96);interviewInput.MaxLength=2000;interviewInput.AcceptsReturn=true;interviewInput.VerticalContentAlignment=VerticalAlignment.Top;
   interviewInput.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   interviewInput.ToolTip="Enter 发送，Shift + Enter 换行；回答会发送给 DeepSeek";
   interviewInput.PreviewKeyDown+=delegate(object o,System.Windows.Input.KeyEventArgs e) {if((e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.Return)&&(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Shift)==0){SendInterview();e.Handled=true;}};
   extractStatus=Ui.Text("",12,"#447969");
   interviewQuestion=Ui.Text("",13,"#59434B");interviewQuestion.Margin=new Thickness(0,0,0,8);
   interviewSend=Ui.Button("回答并整理",delegate {SendInterview();},true);
   interviewAsk=Ui.Button("让曳尔沐提问",delegate {AskNext();},false);
   extractButton=Ui.Button("重新整理最近对话",delegate {ExtractRecent();},false);
   var inputRow=new Grid();inputRow.ColumnDefinitions.Add(new ColumnDefinition());inputRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   inputRow.Children.Add(interviewInput);Grid.SetColumn(interviewInput,0);inputRow.Children.Add(interviewSend);Grid.SetColumn(interviewSend,1);interviewSend.VerticalAlignment=VerticalAlignment.Top;
   focusInput=Ui.Input("",36);focusInput.Width=260;focusInput.MaxLength=100;focusInput.ToolTip="例如：模型部署、需求评审、论文复现";
   var talk=Ui.Stack(
    Ui.Text("逐层了解你的工作经验",18,"#4F3B43"),
    Ui.Text("从最近一项真实工作说起；会什么、哪里不熟悉，都可以直接说。流程会先保存为草稿。",12,"#78616A"),
    Ui.Text("本次想深入的主题（可选）",12,"#78616A"),
    Ui.Row(focusInput,Ui.Button("聚焦这个主题",delegate {if(!string.IsNullOrWhiteSpace(focusInput.Text))archive.Focus(focusInput.Text);},false)),
    extractStatus,interviewQuestion,inputRow,
    Ui.Row(Ui.Button("这部分不熟悉",delegate {interviewInput.Text="这部分我还不熟悉，不能独立完成。";interviewInput.Focus();},false),Ui.Button("跳过这个问题",delegate {archive.Focus(archive.Slot.Focus.Length>0?archive.Slot.Focus:"换一个工作方面，先了解背景");},false),Ui.Button("暂停访谈",delegate {archive.Pause();},false)),
    Ui.Row(interviewAsk,extractButton,Ui.Button("清空访谈记录",ClearInterview,false)),
    Ui.Text("提问和整理会向已配置的 API 发送近期访谈、背景及资料索引。内容同时保存在本机，可随时查看。",11,"#795F69"),
    new Expander{Header="查看访谈记录",Content=interviewLog});
   pages.Items.Add(new TabItem{Header="深入访谈",Content=Ui.Card(talk)});
   profileSummary=Ui.Text("",13,"#59434B");
   profileStats=Ui.Text("",11,"#795F69");
   changeList=new StackPanel();
   profileList=new StackPanel{Margin=new Thickness(0,4,0,10)};
   knowledgeSearch=Ui.Input("",34);knowledgeSearch.ToolTip="搜索领域、知识点、掌握边界";knowledgeSearch.TextChanged+=delegate {RefreshProfile();};
   var profile=Ui.Stack(
    Ui.Text("我的个人画像",18,"#4F3B43"),
    Ui.Text("这些是「存档单元」整理出的知识点，普通聊天时曳尔沐会参考它。",12,"#78616A"),
    profileSummary,profileStats,Ui.Text("搜索知识与边界",12,"#78616A"),knowledgeSearch,profileList,new Expander{Header="知识变更记录 · 查看与恢复（最近 20 次）",Content=changeList},
    Ui.Row(Ui.Button("清空画像",ClearProfile,false),Ui.Button("导出 Markdown",ExportArchive,false)),
    Ui.Text("条目来自对话整理，可能有理解偏差；不想要的可以直接删掉。",11,"#795F69"));
   pages.Items.Add(new TabItem{Header="知识边界",Content=Ui.Card(profile)});
   resourceList=new StackPanel();workflowList=new StackPanel();episodeList=new StackPanel();
   pages.Items.Add(new TabItem{Header="资料库",Content=Ui.Card(Ui.Stack(Ui.Text("按来源归档的资料",18,"#4F3B43"),Ui.Text("在访谈中提供仓库、论文或文档链接及用途。这里保存本地资料卡与经验笔记，可按需读取可用正文并缓存到本地。",12,"#78616A"),resourceList))});
   pages.Items.Add(new TabItem{Header="流程与 Skill",Content=Ui.Card(Ui.Stack(Ui.Text("先核实步骤，再确认复用",18,"#4F3B43"),Ui.Text("逐项检查触发条件、前置条件、步骤、验收和异常处理。确认后写入本地 SKILL.md，并作为后续工作的背景。修改流程后需要重新确认。",12,"#78616A"),Ui.Button("打开本地知识库",delegate {if(pet.IsPreview){archive.Notify("预览模式不写入文件。");return;}archive.Save();string folder=ArchiveKnowledge.Folder(archive.Slot);if(Directory.Exists(folder))Process.Start("explorer.exe",folder);},false),workflowList))});
   pages.Items.Add(new TabItem{Header="案例与复盘",Content=Ui.Card(Ui.Stack(Ui.Text("把真实工作经历变成可复用经验",18,"#4F3B43"),Ui.Text("访谈自动提取有原话依据的场景、做法、结果和经验。也可以在流程页记录使用结果；失败后流程退回待核实。成功案例是用户自述，不代表自动执行验证。",12,"#78616A"),episodeList))});
   graphPanel=new KnowledgeGraphPanel(archive,delegate(string topic){pages.SelectedIndex=0;archive.Focus(topic);});
   pages.Items.Insert(2,new TabItem{Header="知识图谱",Content=Ui.Card(graphPanel.Root)});
   tab.Children.Add(new Expander{Header="存档管理 · 切换 / 新建 / 导出 / 设置",Content=Ui.Card(head),Margin=new Thickness(0,12,0,0)});
   return tab;
  }
  public void Save() {archive.Save();}
  public void Dispose() {archive.Changed-=OnArchiveChanged;if(graphPanel!=null)graphPanel.Dispose();}
  void RefreshAll() {
   seenTurns=-1;seenProfile=-1;seenSlots=-1;
   RefreshArchives();RebuildInterviewLog();RefreshProfile();UpdateStatus();UpdateBusy();UpdateInterviewQuestion();
  }
  void OnArchiveChanged() {
   bool wasBusy=busyShown;busyShown=archive.Busy;
   bool slotsChanged=seenSlots!=archive.SlotRevision;
   bool turnsChanged=seenTurns!=archive.TurnRevision;
   bool profileChanged=seenProfile!=archive.ProfileRevision;
   seenSlots=archive.SlotRevision;seenTurns=archive.TurnRevision;seenProfile=archive.ProfileRevision;
   UpdateStatus();
   if(slotsChanged||profileChanged)RefreshArchives();
   if(turnsChanged)RebuildInterviewLog();
   if(profileChanged)RefreshProfile();
   UpdateBusy();UpdateInterviewQuestion();
   if(wasBusy&&!busyShown&&pet.Panel!=null&&pet.Panel.IsVisible)interviewInput.Focus();
  }
  void RefreshArchives() {
   if(archiveChips==null)return;
   archiveChips.Children.Clear();
   ArchiveBook book=archive.Book;
   ArchiveSlot slot=archive.Slot;
   if(book!=null&&book.Slots!=null) {
    for(int i=0;i<book.Slots.Count;i++) {
     ArchiveSlot item=book.Slots[i];bool active=slot!=null&&item.Id==slot.Id;
     Button chip=Ui.Button((active?"● ":"")+item.Name+"  "+item.Items.Count,delegate {SelectArchive(item);},active);
     chip.FontSize=12;chip.Padding=new Thickness(12,7,12,7);chip.Margin=new Thickness(0,0,8,8);
     archiveChips.Children.Add(chip);
    }
   }
   UpdateArchiveText();
  }
  void UpdateArchiveText() {
   ArchiveSlot slot=archive.Slot;
   if(archiveStatus==null||slot==null)return;
   archiveStatus.Text="当前存档："+slot.Name+" · "+ArchiveStore.Stats(slot)+" · 更新 "+TimeText.Local(slot.Updated);
   archiveNote.Text=pet.IsPreview?"预览示例数据，不会写入磁盘。":"文件："+ArchiveStore.FilePath+(ArchiveStore.LastError.Length>0?" · 上次保存出错："+ArchiveStore.LastError:"");
  }
  void UpdateStatus() {
   if(extractStatus!=null) {
    string text=archive.Status;
    if(text.Length==0&&archive.Slot!=null&&archive.Slot.Turns.Count==0)text="还没有访谈记录。点「让曳尔沐提问」开始。";
    extractStatus.Text=text;
   }
   UpdateArchiveText();
  }
  void UpdateBusy() {
   bool busy=archive.Busy;
   bool ready=!busy&&archive.HasQuestion&&!archive.Slot.Paused;
   if(interviewSend!=null)interviewSend.IsEnabled=ready;
   if(interviewInput!=null)interviewInput.IsEnabled=ready;
   if(interviewAsk!=null){interviewAsk.IsEnabled=!busy;interviewAsk.Content=archive.Slot.Paused?"继续访谈":archive.HasQuestion?"继续当前问题":"开始 / 继续访谈";}
   if(extractButton!=null)extractButton.IsEnabled=!busy;
  }
  void SelectArchive(ArchiveSlot target) {
   if(target==null||archive.Busy)return;
   archive.Select(target);
  }
  void RebuildInterviewLog() {
   if(interviewLog==null)return;
   interviewLog.Clear();
   ArchiveSlot slot=archive.Slot;
   if(slot==null)return;
   int start=Math.Max(0,slot.Turns.Count-40);
   for(int i=start;i<slot.Turns.Count;i++)AppendInterview(slot.Turns[i].Role=="ask"?"曳尔沐":"你",slot.Turns[i].Text);
   interviewLog.ScrollToEnd();
  }
  void UpdateInterviewQuestion() {
   if(interviewQuestion==null)return;
   if(focusStatus!=null)focusStatus.Text=archive.Slot.Name+" · "+(archive.Slot.Paused?"已暂停 · ":"当前主题 · ")+(archive.Slot.Focus.Length==0?"建立工作背景":archive.Slot.Focus);
   string asked=archive.LatestQuestion;
   interviewQuestion.Text=asked.Length>0?"当前问题："+asked:"还没有问题。点「让曳尔沐提问」，由 DeepSeek 主动问一个。";
  }
  void AppendInterview(string who,string text) {
   if(interviewLog==null)return;
   if(interviewLog.Text.Length>9000)interviewLog.Text=interviewLog.Text.Substring(interviewLog.Text.Length-7000);
   if(interviewLog.Text.Length>0)interviewLog.AppendText("\r\n");
   interviewLog.AppendText(who+"："+text);
   interviewLog.ScrollToEnd();
  }
  void RefreshProfile() {
   ArchiveSlot slot=archive.Slot;
   if(profileList==null||slot==null)return;
   profileList.Children.Clear();RefreshKnowledge();
   profileSummary.Text=slot.Summary.Length>0?"画像摘要："+slot.Summary:(slot.Items.Count==0?"还没有画像摘要。点「让曳尔沐提问」，聊聊你已经会的东西。":"还没有画像摘要。已有 "+slot.Items.Count+" 条记录，继续访谈会慢慢拼出来。");
   profileStats.Text=ArchiveStore.Stats(slot)+" · 最近更新 "+TimeText.Local(slot.Updated);
   if(slot.Items.Count==0) {
    profileList.Children.Add(Ui.Text("画像还空着。回答一两个问题，条目就会出现在这里。",12,"#795F69"));
    return;
   }
   var ordered=new List<ArchiveItem>(slot.Items);
   ordered.Sort(delegate(ArchiveItem a,ArchiveItem b){return b.At.CompareTo(a.At);});

   for(int i=0;i<ordered.Count;i++){var item=ordered[i];string query=knowledgeSearch==null?"":knowledgeSearch.Text.Trim();if(query.Length==0||(item.Title+item.Domain+item.Boundary+item.Note).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)profileList.Children.Add(ItemRow(item));}

  }
  void RefreshKnowledge() {
   if(resourceList==null||workflowList==null||archive.Slot==null)return;
   resourceList.Children.Clear();workflowList.Children.Clear();episodeList.Children.Clear();changeList.Children.Clear();
   var slot=archive.Slot;
   for(int i=slot.Changes.Count-1;i>=Math.Max(0,slot.Changes.Count-20);i--) {
    var change=slot.Changes[i];
    var row=Ui.Stack(Ui.Text(change.Action+" · "+TimeText.Local(change.At),12,"#447969"),Ui.Text("之前："+ArchiveLearning.ItemText(change.Before)+"\n之后："+ArchiveLearning.ItemText(change.After),12,"#78616A"));
    if(change.Before!=null&&change.After!=null)row.Children.Add(Ui.Button("恢复此次修改前的记录",delegate {archive.RestoreChange(change);},false));
    changeList.Children.Add(Ui.Card(row));
   }
   if(slot.Changes.Count==0)changeList.Children.Add(Ui.Text("尚无变更记录。相同内容再次整理不会新增记录。",12,"#795F69"));
   for(int i=slot.Episodes.Count-1;i>=0;i--) {
    var episode=slot.Episodes[i];
    episodeList.Children.Add(Ui.Card(Ui.Stack(Ui.Text(ArchiveLearning.EpisodeText(episode),13,"#4F3B43"),Ui.Text(TimeText.Local(episode.At),11,"#795F69"),Ui.Row(Ui.Button("深入这个案例",delegate {archive.Focus(episode.Task);},false),Ui.Button("移除案例",delegate {archive.RemoveEpisode(episode);},false)))));
   }
   if(slot.Episodes.Count==0)episodeList.Children.Add(Ui.Text("例如：讲一次你做过的技术选型、故障排查或项目交付，以及为什么这样做。",12,"#795F69"));
   if(archive.Slot.Resources.Count==0)resourceList.Children.Add(Ui.Text("还没有资料。聊天时可以粘贴链接，并说明它在哪项工作中有用。",12,"#795F69"));
   foreach(var resource in archive.Slot.Resources) {
    var r=resource;
    var content=Ui.Stack(Ui.Text(r.Kind+" · "+r.Title,15,"#4F3B43"),Ui.Text(r.Url.Length==0?"链接待补充":r.Url,12,"#78616A"),Ui.Text(r.Note,12,"#78616A"),Ui.Row(Ui.Button(r.Content.Length>0?"刷新本地正文":"读取并保存正文",delegate {archive.FetchResource(r);},false),Ui.Button("补充 / 纠正",delegate {archive.Focus("补充资料："+r.Title);},false),Ui.Button("移除",delegate {archive.RemoveResource(r);},false)));
    resourceList.Children.Add(Ui.Card(content));
   }
   if(archive.Slot.Workflows.Count==0)workflowList.Children.Add(Ui.Text("还没有流程。告诉她一个经常重复的工作，我们会从第一步逐渐核实。",12,"#795F69"));
   foreach(var workflow in archive.Slot.Workflows) {
    var w=workflow;
    var outcome=new ComboBox{Width=100,Margin=new Thickness(0,0,8,8),ItemsSource=new[]{"未验证","成功","失败"},SelectedIndex=0};
    var note=Ui.Input("",65);note.AcceptsReturn=true;note.MaxLength=500;note.ToolTip="具体结果、失败原因或下次应注意的条件";
    var feedback=new Expander{Header="记录这次使用结果",Content=Ui.Stack(Ui.Text("具体结果 / 问题 / 改进经验",12,"#78616A"),note,Ui.Row(outcome,Ui.Button("保存复盘",delegate {if(archive.RecordFeedback(w,(string)outcome.SelectedItem,note.Text))note.Clear();},false)))};
    workflowList.Children.Add(Ui.Card(Ui.Stack(Ui.Text((w.Confirmed?"已确认 · ":"待核实 · ")+w.Title,15,"#4F3B43"),Ui.Text(ArchiveKnowledge.WorkflowText(w),12,"#78616A"),Ui.Row(Ui.Button(w.Confirmed?"退回草稿":"我已核实，确认流程",delegate {archive.ConfirmWorkflow(w);},true),Ui.Button("逐步补充 / 纠正",delegate {archive.Focus("逐步核实流程："+w.Title);},false),Ui.Button("移除",delegate {archive.RemoveWorkflow(w);},false)),feedback)));
   }
  }
  UIElement ItemRow(ArchiveItem item) {
   var grid=new Grid();
   grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   grid.ColumnDefinitions.Add(new ColumnDefinition());
   grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var level=new Border{Background=Ui.Brush("#F0E6F5"),CornerRadius=new CornerRadius(8),Padding=new Thickness(8,3,8,3),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(12,11,10,11),Child=new TextBlock{Text=item.Boundary+"\n"+item.Level,FontSize=11,Foreground=Ui.Brush("#78669B")}};
   Grid.SetColumn(level,0);grid.Children.Add(level);
   var body=new StackPanel{Margin=new Thickness(0,10,10,10)};
   body.Children.Add(new TextBlock{Text=item.Title,FontSize=13,Foreground=Ui.Brush("#4F3B43"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3)});
   body.Children.Add(new TextBlock{Text=item.Domain+" · "+TimeText.Local(item.At)+" · "+item.Source,FontSize=11,Foreground=Ui.Brush("#795F69"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3)});
   if(item.Evidence.Length>0)body.Children.Add(new TextBlock{Text="依据："+item.Evidence,FontSize=11,Foreground=Ui.Brush("#78616A"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3)});
   if(item.Note.Length>0)body.Children.Add(new TextBlock{Text="备注："+item.Note,FontSize=11,Foreground=Ui.Brush("#795F69"),TextWrapping=TextWrapping.Wrap});
   Grid.SetColumn(body,1);grid.Children.Add(body);
   Button remove=Ui.Button("删除",delegate {DeleteItem(item);},false);
   remove.FontSize=11;remove.Padding=new Thickness(10,6,10,6);remove.Margin=new Thickness(0,10,10,10);remove.VerticalAlignment=VerticalAlignment.Top;
   var actions=new StackPanel();actions.Children.Add(Ui.Button("纠正",delegate {archive.Focus("核实知识边界："+item.Domain+" / "+item.Title);},false));actions.Children.Add(remove);
   Grid.SetColumn(actions,2);grid.Children.Add(actions);
   return new Border{Background=Ui.Brush("#FFFCF9"),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,0,8),Child=grid};
  }
  void DeleteItem(ArchiveItem item) {archive.DeleteItem(item);}
  void NewArchive() {
   string name=(archiveName.Text??"").Trim();
   archive.Create(name);archiveName.Clear();
  }
  void RenameArchive() {
   if(archive.Slot==null)return;
   string name=(archiveName.Text??"").Trim();
   if(name.Length==0) {Ask("先在输入框里写下新名字，再点「重命名」。","重命名存档");return;}
   if(!archive.Rename(name))return;
   archiveName.Clear();
  }
  void DeleteArchive() {
   ArchiveSlot slot=archive.Slot;
   if(slot==null||archive.Busy)return;
   if(archive.Book.Slots.Count<=1) {Ask("至少保留一个存档。可以先新建一个，再删除这个。","删除存档");return;}
   if(Ask("删除存档「"+slot.Name+"」以及其中的 "+slot.Items.Count+" 条画像记录？\n这个操作不能撤销。","删除存档",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
   archive.Delete();
  }
  void ExportArchive() {
   ArchiveSlot slot=archive.Slot;
   if(slot==null)return;
   if(pet.IsPreview) {archive.Notify("预览模式不导出文件。");return;}
   try {
    string path=ArchiveStore.Export(slot);
    if(path.Length==0) {archive.Notify("导出失败："+ArchiveStore.LastError);return;}
    archive.Notify("已导出："+path);
    try {Process.Start("explorer.exe","/select,\""+path+"\"");} catch {}
   } catch(Exception e) {archive.Notify("导出失败："+e.Message);}
  }
  void ClearProfile() {
   ArchiveSlot slot=archive.Slot;
   if(slot==null||slot.Items.Count==0) {archive.Notify("画像已经是空的。");return;}
   if(Ask("清空「"+slot.Name+"」的个人画像？\n会删除全部 "+slot.Items.Count+" 条知识记录，访谈记录会保留。","清空画像",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
   archive.ClearProfile();
  }
  void ClearInterview() {archive.ClearTurns();}
  void AskNext() {
   if(archive.Busy)return;
   archive.AskNext();
  }
  void SendInterview() {
   if(archive.Busy)return;
   if(!archive.HasQuestion) {archive.Notify("先让曳尔沐问一个问题，再回答。");return;}
   string text=(interviewInput.Text??"").Trim();
   if(text.Length==0)return;
   if(archive.Answer(text))interviewInput.Clear();
   if(!archive.Busy&&pet.Panel!=null&&pet.Panel.IsVisible)interviewInput.Focus();
  }
  void ExtractRecent() {archive.ExtractRecent();}
 }
}
