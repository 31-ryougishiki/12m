using System;
using System.Windows;
using System.Windows.Controls;
namespace ElymsPet {
 public sealed class ArchiveCloudPanel {
  readonly ArchiveService archive;readonly TextBlock status;readonly ComboBox versions;
  public UIElement Root {get;private set;}
  public ArchiveCloudPanel(ArchiveService service){
   archive=service;var config=archive.CloudConfig;
   var bucket=Ui.Input(config.Bucket,44);var region=Ui.Input(config.Region,44);var prefix=Ui.Input(config.Prefix,44);
   var key=Ui.Input("",44);var secret=new PasswordBox{Height=36,Margin=new Thickness(0,4,0,8)};
   var automatic=new CheckBox{Content="每两分钟自动上传变化",IsChecked=config.Automatic,Margin=new Thickness(0,8,0,12)};
   status=Ui.Text(archive.CloudStatus,14,"#447969");versions=new ComboBox{MinWidth=280,MaxWidth=650,Margin=new Thickness(0,6,0,12)};
   var settings=Ui.Stack(Ui.Text("Bucket",13,"#78616A"),bucket,Ui.Text("地域",13,"#78616A"),region,Ui.Text("目录",13,"#78616A"),prefix,Ui.Text("AccessKey ID / Secret（留空保留已保存凭证）",13,"#78616A"),key,secret,automatic,Ui.Button("保存设置",delegate{
    if(archive.CloudBusy)return;var next=new ArchiveCloudConfig{Bucket=bucket.Text.Trim(),Region=region.Text.Trim(),Prefix=prefix.Text.Trim(),Automatic=automatic.IsChecked==true,ProtectedCredentials=archive.CloudConfig.ProtectedCredentials};
    if(key.Text.Length>0||secret.Password.Length>0){if(string.IsNullOrWhiteSpace(key.Text)||string.IsNullOrWhiteSpace(secret.Password)){status.Text="请同时填写 ID 和 Secret。";return;}next.SetCredentials(key.Text,secret.Password);}
    if(archive.ConfigureCloud(next)){key.Clear();secret.Clear();}
   },true));
   Root=Ui.Card(Ui.Stack(Ui.Text("存档云同步",20,"#4F3B43"),Ui.Text("保存独立版本，需要时选择恢复。",14,"#78616A"),status,
    Ui.Row(Ui.Button("立即上传",delegate{archive.UploadCloud(true);},true),Ui.Button("查看云端版本",delegate{archive.ListCloud(delegate(System.Collections.Generic.List<ArchiveCloudVersion> list){versions.ItemsSource=list;if(list.Count>0)versions.SelectedIndex=0;});},false)),versions,
    Ui.Button("恢复所选版本",delegate{var selected=versions.SelectedItem as ArchiveCloudVersion;if(selected==null||archive.CloudBusy)return;if(MessageBox.Show("用所选版本替换所有存档？恢复前会保留本地备份。","恢复存档",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)archive.RestoreCloud(selected);},false),
    new Expander{Header="连接与自动上传设置",Content=settings,IsExpanded=string.IsNullOrEmpty(config.ProtectedCredentials),Margin=new Thickness(0,12,0,0)}));archive.Changed+=Refresh;
  }
  void Refresh(){status.Text=archive.CloudStatus;}
  public void Dispose(){archive.Changed-=Refresh;}
 }
}
