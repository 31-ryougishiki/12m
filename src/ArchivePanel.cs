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
  readonly Action<int> selectTab;
  readonly ArchiveService archive;
  WrapPanel archiveChips;
  TextBox archiveName,interviewLog,interviewInput;
  TextBlock archiveStatus,archiveNote,extractStatus,profileSummary,profileStats,interviewQuestion;
  StackPanel profileList;
  Button interviewSend,interviewAsk,extractButton;
  int seenTurns=-1,seenProfile=-1,seenSlots=-1;
  bool busyShown;
  public UIElement Root {get; private set;}
  public ArchivePanel(PetWindow target,Action<int> selectTab) {
   pet=target;this.selectTab=selectTab;archive=pet.Archive;
   Root=Build();
   archive.Changed+=OnArchiveChanged;
   RefreshAll();
  }
  Window Owner {get {return pet.Panel;}}
  MessageBoxResult Ask(string text,string title) {
   return Owner==null?MessageBox.Show(text,title):MessageBox.Show(Owner,text,title);
  }
  MessageBoxResult Ask(string text,string title,MessageBoxButton buttons,MessageBoxImage icon) {
   return Owner==null?MessageBox.Show(text,title,buttons,icon):MessageBox.Show(Owner,text,title,buttons,icon);
  }
  /// <summary>构建页面内容；由陪伴面板放进「存档单元」标签页。</summary>
  UIElement Build() {
   var tab=new StackPanel();
   archiveChips=new WrapPanel{Margin=new Thickness(0,2,0,4)};
   archiveName=Ui.Input("",40);archiveName.Width=200;archiveName.MaxLength=24;archiveName.Margin=new Thickness(0,0,9,9);
   archiveName.ToolTip="写新存档的名字；留空会自动命名为「存档 2」";
   archiveStatus=Ui.Text("",12,"#C8D2EB");
   archiveNote=Ui.Text("",11,"#8FA4C7");
   var head=Ui.Stack(
    Ui.Text("存档单元 · 你的个人画像",18,"#E7EDFC"),
    Ui.Text("用对话把已经掌握的知识存进个人画像。接入 DeepSeek 后由曳尔沐提问并自动整理；没有密钥时改用离线问答，一样能记录。",12,"#A6B5CF"),
    Ui.Text("选择存档",12,"#A6B5CF"),
    archiveChips,
    Ui.Text("新建 / 重命名用的名字（留空时自动命名）",11,"#8FA4C7"),
    Ui.Row(archiveName,Ui.Button("新建存档",NewArchive,true),Ui.Button("重命名",RenameArchive,false),Ui.Button("删除存档",DeleteArchive,false)),
    Ui.Row(Ui.Button("导出 Markdown",ExportArchive,false),Ui.Button("打开数据文件夹",delegate {Directory.CreateDirectory(ArchiveStore.Folder);Process.Start("explorer.exe",ArchiveStore.Folder);},false),Ui.Button("接口设置",delegate {selectTab(7);},false)),
    archiveStatus,archiveNote);
   tab.Children.Add(Ui.Card(head));
   interviewLog=Ui.Input("",130);interviewLog.IsReadOnly=true;interviewLog.AcceptsReturn=true;interviewLog.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;interviewLog.VerticalContentAlignment=VerticalAlignment.Top;interviewLog.FontSize=13;interviewLog.Margin=new Thickness(0,0,0,4);
   interviewLog.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   interviewInput=Ui.Input("",44);interviewInput.MaxLength=2000;interviewInput.AcceptsReturn=true;interviewInput.VerticalContentAlignment=VerticalAlignment.Top;
   interviewInput.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),Theme.ScrollBarStyle);
   interviewInput.ToolTip="Enter 发送，Shift + Enter 换行；回答会发送给 DeepSeek";
   interviewInput.PreviewKeyDown+=delegate(object o,System.Windows.Input.KeyEventArgs e) {if((e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.Return)&&(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Shift)==0){SendInterview();e.Handled=true;}};
   extractStatus=Ui.Text("",12,"#9FE5D7");
   interviewQuestion=Ui.Text("",13,"#DCE5F7");interviewQuestion.Margin=new Thickness(0,0,0,8);
   interviewSend=Ui.Button("回答并整理",delegate {SendInterview();},true);
   interviewAsk=Ui.Button("让曳尔沐提问",delegate {AskNext();},false);
   extractButton=Ui.Button("重新整理最近对话",delegate {ExtractRecent();},false);
   var inputRow=new Grid();inputRow.ColumnDefinitions.Add(new ColumnDefinition());inputRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   inputRow.Children.Add(interviewInput);Grid.SetColumn(interviewInput,0);inputRow.Children.Add(interviewSend);Grid.SetColumn(interviewSend,1);interviewSend.VerticalAlignment=VerticalAlignment.Top;
   var talk=Ui.Stack(
    Ui.Text("画像整理对话",18,"#E7EDFC"),
    Ui.Text("回答她一个问题，整理出的条目会立刻写进当前存档。",12,"#A6B5CF"),
    extractStatus,interviewQuestion,inputRow,
    Ui.Row(interviewAsk,extractButton,Ui.Button("清空访谈记录",ClearInterview,false)),
    Ui.Text("访谈记录和画像保存在本机数据文件夹。只有你按下发送时，回答才会发送给 DeepSeek。",11,"#8FA4C7"),
    Ui.Text("访谈记录",12,"#A6B5CF"),
    interviewLog);
   tab.Children.Add(Ui.Card(talk));
   profileSummary=Ui.Text("",13,"#DCE5F7");
   profileStats=Ui.Text("",11,"#8FA4C7");
   profileList=new StackPanel{Margin=new Thickness(0,4,0,10)};
   var profile=Ui.Stack(
    Ui.Text("我的个人画像",18,"#E7EDFC"),
    Ui.Text("这些是「存档单元」整理出的知识点，普通聊天时曳尔沐会参考它。",12,"#A6B5CF"),
    profileSummary,profileStats,profileList,
    Ui.Row(Ui.Button("清空画像",ClearProfile,false),Ui.Button("导出 Markdown",ExportArchive,false)),
    Ui.Text("条目来自对话整理，可能有理解偏差；不想要的可以直接删掉。",11,"#8FA4C7"));
   tab.Children.Add(Ui.Card(profile));
   return tab;
  }
  public void Save() {archive.Save();}
  public void Dispose() {archive.Changed-=OnArchiveChanged;}
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
   bool ready=!busy&&archive.HasQuestion;
   if(interviewSend!=null)interviewSend.IsEnabled=ready;
   if(interviewInput!=null)interviewInput.IsEnabled=ready;
   if(interviewAsk!=null)interviewAsk.IsEnabled=!busy;
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
   profileList.Children.Clear();
   profileSummary.Text=slot.Summary.Length>0?"画像摘要："+slot.Summary:(slot.Items.Count==0?"还没有画像摘要。点「让曳尔沐提问」，聊聊你已经会的东西。":"还没有画像摘要。已有 "+slot.Items.Count+" 条记录，继续访谈会慢慢拼出来。");
   profileStats.Text=ArchiveStore.Stats(slot)+" · 最近更新 "+TimeText.Local(slot.Updated);
   if(slot.Items.Count==0) {
    profileList.Children.Add(Ui.Text("画像还空着。回答一两个问题，条目就会出现在这里。",12,"#8FA4C7"));
    return;
   }
   var ordered=new List<ArchiveItem>(slot.Items);
   ordered.Sort(delegate(ArchiveItem a,ArchiveItem b){return b.At.CompareTo(a.At);});
   int shown=Math.Min(ordered.Count,80);
   for(int i=0;i<shown;i++)profileList.Children.Add(ItemRow(ordered[i]));
   if(ordered.Count>shown)profileList.Children.Add(Ui.Text("还有 "+(ordered.Count-shown)+" 条没有显示，导出 Markdown 可以看到全部。",11,"#8FA4C7"));
  }
  UIElement ItemRow(ArchiveItem item) {
   var grid=new Grid();
   grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   grid.ColumnDefinitions.Add(new ColumnDefinition());
   grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var level=new Border{Background=Ui.Brush("#23364F"),CornerRadius=new CornerRadius(8),Padding=new Thickness(8,3,8,3),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(12,11,10,11),Child=new TextBlock{Text=item.Level,FontSize=11,Foreground=Ui.Brush("#AFE6DA")}};
   Grid.SetColumn(level,0);grid.Children.Add(level);
   var body=new StackPanel{Margin=new Thickness(0,10,10,10)};
   body.Children.Add(new TextBlock{Text=item.Title,FontSize=13,Foreground=Ui.Brush("#E6EDFF"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3)});
   body.Children.Add(new TextBlock{Text=item.Domain+" · "+TimeText.Local(item.At)+" · "+item.Source,FontSize=11,Foreground=Ui.Brush("#8FA4C7"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3)});
   if(item.Evidence.Length>0)body.Children.Add(new TextBlock{Text="依据："+item.Evidence,FontSize=11,Foreground=Ui.Brush("#A9BBD8"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,3)});
   if(item.Note.Length>0)body.Children.Add(new TextBlock{Text="备注："+item.Note,FontSize=11,Foreground=Ui.Brush("#7E93B5"),TextWrapping=TextWrapping.Wrap});
   Grid.SetColumn(body,1);grid.Children.Add(body);
   Button remove=Ui.Button("删除",delegate {DeleteItem(item);},false);
   remove.FontSize=11;remove.Padding=new Thickness(10,6,10,6);remove.Margin=new Thickness(0,10,10,10);remove.VerticalAlignment=VerticalAlignment.Top;
   Grid.SetColumn(remove,2);grid.Children.Add(remove);
   return new Border{Background=Ui.Brush("#111D31"),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,0,8),Child=grid};
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
   interviewInput.Clear();
   archive.Answer(text);
   if(!archive.Busy&&pet.Panel!=null&&pet.Panel.IsVisible)interviewInput.Focus();
  }
  void ExtractRecent() {archive.ExtractRecent();}
 }
}
