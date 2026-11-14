using System;
using System.Windows;
using System.Windows.Controls;
namespace ElymsPet {
 public sealed class PluginPanel {
  readonly PetWindow pet;readonly Action<PanelTab> navigate;readonly StackPanel content=new StackPanel();
  public UIElement Root {get{return content;}}
  public PluginPanel(PetWindow pet,Action<PanelTab> navigate){this.pet=pet;this.navigate=navigate;Refresh();}
  public void Refresh(){
   content.Children.Clear();var manager=pet.State.Plugins;
   content.Children.Add(Ui.Text("快捷入口与插件",24,"#334363"));
   content.Children.Add(Ui.Text("官方插件 · 从随应用提供的安装包安装，无需额外下载。安装、停用和卸载在重启后生效；卸载保留存档、图谱、登录及连接配置。",14,"#596784"));
   if(manager.PendingRestart)content.Children.Add(Ui.Button("重启曳尔沐并应用",Restart,true));
   content.Children.Add(Ui.Text(manager.Status,13,"#447969"));
   var entries=new WrapPanel();
   foreach(var entry in RadialMenuLayout.Entries){var item=entry;if(!PluginAccess.Page(pet.State,item.Page))continue;
    bool visible=RadialMenuLayout.Visible(pet.State.RadialPages,item.Page);
    entries.Children.Add(Ui.Button((visible?"− ":"＋ ")+item.Label,delegate{ToggleEntry(item.Page);},false));
   }
   content.Children.Add(Ui.Stack(Ui.Text("快捷入口 · 无需安装的功能可直接添加",17,"#334363"),Ui.Text("点＋添加，点−移出圆盘。移出不会停用或卸载功能；保留一个空位方便继续添加。",12,"#596784"),entries));
   content.Children.Add(Ui.Text("可选插件",19,"#334363"));
   foreach(var package in manager.Catalog){var p=package;var buttons=new WrapPanel();
    buttons.Children.Add(Ui.Button(manager.Installed(p.Id)?"更新 / 修复":"安装",delegate{Change(p.Id,"install");},!manager.Installed(p.Id)));
    if(manager.Installed(p.Id)){buttons.Children.Add(Ui.Button(manager.DesiredEnabled(p.Id)?"停用":"启用",delegate{Change(p.Id,manager.DesiredEnabled(p.Id)?"disable":"enable");},false));buttons.Children.Add(Ui.Button("卸载（保留数据）",delegate{Change(p.Id,"remove");},false));}
    if(manager.Enabled(p.Id)){string id=p.Id;buttons.Children.Add(Ui.Button("打开",delegate{navigate(PageFor(id));},false));}
    content.Children.Add(Ui.Card(Ui.Stack(Ui.Text(p.Name+"  ·  "+p.Version,19,"#334363"),Ui.Text(p.Description,14,"#596784"),new Expander{Header="使用范围",Content=Ui.Text(p.Permissions,12,"#596784")},Ui.Text(manager.StateText(p.Id)+(manager.PendingRestart?"（本次更改待重启）":""),13,"#447969"),buttons)));
   }

  }
  public static PanelTab PageFor(string id){switch(id){case "archive":return PanelTab.Archive;case "graph":return PanelTab.Graph;case "bili":return PanelTab.Bili;case "dsh":return PanelTab.Workbench;case "cloud":return PanelTab.Cloud;default:throw new ArgumentException("未知插件");}}
  void ToggleEntry(PanelTab page){
   bool show=!RadialMenuLayout.Visible(pet.State.RadialPages,page);
   if(show){int count=0;foreach(var item in RadialMenuLayout.Entries)if(PluginAccess.Page(pet.State,item.Page)&&RadialMenuLayout.Visible(pet.State.RadialPages,item.Page))count++;if(count>=7){PopupDialog.Show(Window.GetWindow(Root),"先移出一个不常用的入口，再添加新功能。圆盘会保留一个＋入口。","圆盘已满");return;}}
   pet.State.RadialPages=RadialMenuLayout.Toggle(pet.State.RadialPages,page,show);pet.Save();if(pet.Menu!=null)pet.Menu.RefreshEntries();Refresh();
  }
  bool Busy(){return pet.Conversation.Busy||pet.Archive.Busy||pet.Archive.CloudBusy||pet.Bili.Busy||pet.Dsh.Busy;}
  void Change(string id,string action){if(Busy()){PopupDialog.Show(Window.GetWindow(Root),"请等待当前任务结束，再更改插件。","插件正在使用");return;}if(pet.State.Plugins.Change(id,action)&&action=="install"){PanelTab page=PageFor(id);foreach(var entry in RadialMenuLayout.Entries)if(entry.Page==page)pet.State.RadialPages=RadialMenuLayout.Toggle(pet.State.RadialPages,page,true);pet.Save();}Refresh();}
  void Restart(){if(Busy()){PopupDialog.Show(Window.GetWindow(Root),"当前任务尚未结束，请完成后重启。","暂不能重启");return;}if(pet.Panel!=null)pet.Panel.Close();pet.Save();System.Diagnostics.Process.Start(System.Reflection.Assembly.GetEntryAssembly().Location,"--restart-after "+System.Diagnostics.Process.GetCurrentProcess().Id);pet.Close();}
  public static PluginPage Missing(PetWindow pet,string id,Action<PanelTab> navigate){return new PluginPage{Root=Ui.Card(Ui.Stack(Ui.Text(pet.State.Plugins.Package(id).Name,22,"#334363"),Ui.Text(pet.State.Plugins.StateText(id)+"。安装并启用后即可使用，已有数据保留。",14,"#596784"),Ui.Button("管理插件",delegate{navigate(PanelTab.Plugins);},true)))};}
 }
}
