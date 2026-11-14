using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace ElymsPet {
 public sealed partial class KnowledgeGraphPanel {
  static TextBlock ReadableText(string value){
   var text=Text("",15,"#33405B");text.LineHeight=25;
   string[] spans=(value??"").Split('`');for(int i=0;i<spans.Length;i++){var run=new Run(spans[i]);if(i%2==1){run.FontFamily=new FontFamily("Consolas");run.Foreground=Theme.Brush("#176459");run.Background=Theme.Brush("#EDF4F8");}text.Inlines.Add(run);}return text;
  }
  static UIElement ApproachCard(string part,int index){
   var box=new StackPanel();box.Children.Add(Text("要点 "+(index+1).ToString("00"),12,"#49788F"));box.Children.Add(ReadableText(part));
   return new Border{Background=Theme.Brush("#F5F8FC"),BorderBrush=Theme.Brush("#D4DCEE"),BorderThickness=new Thickness(2,0,0,0),Padding=new Thickness(12,8,10,2),Margin=new Thickness(0,0,0,10),Child=box};
  }
  static void ApproachPanel(StackPanel panel,string value){
   panel.Children.Add(Text("实现思路与使用条件",12,"#49788F"));var parts=LearningReadability.Parts(value);
   if(parts.Count==0){panel.Children.Add(Text("尚未补充",14,"#687794"));return;}
   int shown=0,characters=0;while(shown<parts.Count&&shown<2&&(characters+parts[shown].Length<=320)){panel.Children.Add(ApproachCard(parts[shown],shown));characters+=parts[shown].Length;shown++;}
   if(shown<parts.Count){var more=new StackPanel{Margin=new Thickness(0,10,0,0)};for(int i=shown;i<parts.Count;i++)more.Children.Add(ApproachCard(parts[i],i));panel.Children.Add(new Expander{Header=shown==0?"展开实现细节 · "+parts.Count+" 个要点":"继续阅读 · 其余 "+(parts.Count-shown)+" 个要点",Content=more,IsExpanded=false,Margin=new Thickness(0,0,0,12)});}
  }
  void OpenLocalCode(string url,TextBlock notice){
   var settings=LocalRepository.Load();string folder=LocalRepository.MappedFolder(settings,url);
   if(string.IsNullOrWhiteSpace(folder)||string.IsNullOrWhiteSpace(settings.Editor)){ConfigureLocalRepository(url);return;}
   notice.Visibility=Visibility.Visible;notice.Text="正在核对本地文件版本…";
   ThreadPool.QueueUserWorkItem(delegate{string message;try{var target=LocalRepository.Resolve(folder,url);System.Diagnostics.Process.Start(LocalRepository.OpenInfo(settings.Editor,target));message=target.Message;}catch(Exception ex){message="本地跳转未完成："+ex.Message;}
    if(canvas.Dispatcher.HasShutdownStarted)return;canvas.Dispatcher.BeginInvoke(new Action(delegate{if(!disposed)notice.Text=message;}));});
  }
  void ConfigureLocalRepository(string url){
   var settings=LocalRepository.Load();string repository=LocalRepository.Repository(url);var folder=Ui.Input(LocalRepository.MappedFolder(settings,url),55);var editor=Ui.Input(string.IsNullOrEmpty(settings.Editor)?LocalRepository.FindEditor():settings.Editor,55);
   var dialog=new Window{Owner=LearningOwner,Title="本地源码跳转",Width=660,Height=580,MaxHeight=SystemParameters.WorkArea.Height-40,Background=Theme.Canvas,WindowStartupLocation=WindowStartupLocation.CenterOwner};
   var error=Text("",13,"#AD506B");var browseFolder=EditAction("选择文件夹",delegate{using(var picker=new System.Windows.Forms.FolderBrowserDialog{Description="选择包含 .git 的仓库根目录",SelectedPath=Directory.Exists(folder.Text)?folder.Text:"",ShowNewFolderButton=false})if(picker.ShowDialog()==System.Windows.Forms.DialogResult.OK)folder.Text=picker.SelectedPath;});
   var browseEditor=EditAction("选择编辑器",delegate{var picker=new Microsoft.Win32.OpenFileDialog{Filter="编辑器|Code.exe;Cursor.exe",CheckFileExists=true};if(picker.ShowDialog(dialog)==true)editor.Text=picker.FileName;});
   var content=Ui.Stack(Text(repository,22,"#28324A"),Text("本地仓库根目录",14,"#33405B"),folder,browseFolder,Text("编辑器程序（VS Code / Cursor）",14,"#33405B"),editor,browseEditor,Text("文件与来源提交一致时定位原行号；文件变化或版本无法核实时打开文件开头。这里只读取文件，不切换分支。路径配置仅保存在本机。",13,"#687794"),error,Button("保存映射",delegate{
    try{string root=Path.GetFullPath(folder.Text.Trim());if(!Directory.Exists(Path.Combine(root,".git"))&&!File.Exists(Path.Combine(root,".git")))throw new InvalidDataException("请选择 Git 仓库根目录（包含 .git）。");string file=LocalRepository.ResolveFile(root,url);LocalRepository.OpenInfo(editor.Text.Trim(),new LocalCodeTarget{File=file,Line=1});
     var fresh=LocalRepository.Load();fresh.Editor=editor.Text.Trim();fresh.Repositories.RemoveAll(delegate(LocalRepositoryMapping m){return m!=null&&string.Equals(m.Repository,repository,StringComparison.OrdinalIgnoreCase);});fresh.Repositories.Add(new LocalRepositoryMapping{Repository=repository,Folder=root});string failure;if(!LocalRepository.Save(fresh,out failure)){error.Text="保存失败："+failure;return;}status.Text="已保存本地仓库映射，点击「本地打开」即可跳转。";dialog.Close();
    }catch(Exception ex){error.Text=ex.Message;}}));dialog.Content=new ScrollViewer{Content=new Border{Padding=new Thickness(24),Child=content},VerticalScrollBarVisibility=ScrollBarVisibility.Auto};GraphCopyText.Enable(dialog);dialog.ShowDialog();
  }
 }
}
