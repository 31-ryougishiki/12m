using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Windows;
namespace ElymsPet {
 public sealed class PluginPage : IDisposable {
  public UIElement Root; public Action Save=delegate{},Refresh=delegate{},Close=delegate{};public Action<string> StartTopic=delegate{};
  public void Dispose(){Close();}
 }
 public interface IOfficialPlugin {PluginPage Create(PetWindow pet,Action<PanelTab> navigate);}
 [DataContract] public sealed class PluginPackage {
  [DataMember] public string Id,Name,Description,Permissions,Version,Hash;
  [DataMember] public int Api;
 }
 [DataContract] public sealed class PluginInstall {[DataMember] public string Id,Hash,Version;[DataMember] public bool Enabled;}
 [DataContract] public sealed class PluginRegistry {[DataMember] public List<PluginInstall> Items=new List<PluginInstall>();}
 public static class PluginAccess {
  // Standalone domain services/tests have no host. The desktop host always supplies a manager.
  public static bool Enabled(PetState state,string id){return state.Plugins==null||state.Plugins.Enabled(id);}
  public static string ForPage(PanelTab page){switch(page){case PanelTab.Archive:return "archive";case PanelTab.Graph:return "graph";case PanelTab.Bili:return "bili";case PanelTab.Workbench:return "dsh";case PanelTab.Cloud:return "cloud";default:return "";}}
  public static bool Page(PetState state,PanelTab page){string id=ForPage(page);return id.Length==0||Enabled(state,id);}
 }
 public sealed class PluginManager {
  public const int ApiVersion=1;
  public readonly List<PluginPackage> Catalog;
  readonly string root,packages,registryPath;readonly bool preview;
  PluginRegistry registry;readonly HashSet<string> active=new HashSet<string>();readonly Dictionary<string,string> paths=new Dictionary<string,string>(),errors=new Dictionary<string,string>();
  public bool PendingRestart {get;private set;}
  public string Status {get;private set;}
  public PluginManager(string dataFolder,string applicationFolder,bool preview){
   this.preview=preview;root=Path.Combine(dataFolder,"plugins");registryPath=Path.Combine(root,"installed.json");packages=Path.Combine(applicationFolder,"plugin-packages");Status="";
   using(var stream=typeof(PluginManager).Assembly.GetManifestResourceStream("official-plugins.json")) {
    Catalog=stream==null?new List<PluginPackage>():new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(List<PluginPackage>)).ReadObject(stream) as List<PluginPackage>;
   }
   string error;registry=preview?new PluginRegistry():JsonFile.Load<PluginRegistry>(registryPath,out error)??new PluginRegistry();
   if(registry.Items==null)registry.Items=new List<PluginInstall>();
   foreach(var p in Catalog){var entry=Find(p.Id);if(!preview&&(entry==null||!entry.Enabled))continue;
    try{string path=preview?PackagePath(p):InstalledPath(p.Id,entry.Hash);Verify(p,path);active.Add(p.Id);paths[p.Id]=path;}
    catch(Exception ex){errors[p.Id]=ex.Message;}
   }
   if(!preview)CleanRemovedFiles();
  }
  public static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
  static bool ValidHash(string value){return value!=null&&System.Text.RegularExpressions.Regex.IsMatch(value,"^[a-f0-9]{64}$");}
  PluginInstall Find(string id){return registry.Items.Find(x=>x!=null&&x.Id==id);}
  public PluginPackage Package(string id){var p=Catalog.Find(x=>x.Id==id);if(p==null)throw new InvalidDataException("未知官方插件。");return p;}
  string PackagePath(PluginPackage p){return Path.Combine(packages,p.Id,"module.dll");}
  string InstalledPath(string id,string hash){Package(id);if(!ValidHash(hash))throw new InvalidDataException("插件校验信息无效，请重新安装。");return Path.Combine(root,"installed",id,hash,"module.dll");}
  void Verify(PluginPackage p,string path){if(p.Api!=ApiVersion)throw new InvalidDataException("插件接口版本不兼容。");if(!File.Exists(path)||!ValidHash(p.Hash)||Hash(path)!=p.Hash)throw new InvalidDataException("插件文件缺失或版本不匹配，请安装 / 修复。");}
  public bool Enabled(string id){return active.Contains(id);}
  public bool Installed(string id){return Find(id)!=null;}
  public bool DesiredEnabled(string id){var item=Find(id);return item!=null&&item.Enabled;}
  public string StateText(string id){string error;if(errors.TryGetValue(id,out error))return error;var item=Find(id);return item==null?"未安装":item.Hash!=Package(id).Hash?"有更新，需要修复":item.Enabled?"已安装 · 已启用":"已安装 · 已停用";}
  bool Persist(PluginRegistry next){string error;if(!JsonFile.Save(registryPath,next,root,out error)){Status="未应用："+error;return false;}registry=next;PendingRestart=true;Status="已保存，重启桌宠后生效。数据和账号配置保留。";return true;}
  PluginRegistry Copy(){return GraphEditing.Parse<PluginRegistry>(GraphEditing.Json(registry));}
  public bool Change(string id,string action){
   if(preview){Status="预览模式不修改插件安装状态。";return false;}
   try{var p=Package(id);var next=Copy();var item=next.Items.Find(x=>x!=null&&x.Id==id);
    if(action=="install"){
     string source=PackagePath(p);Verify(p,source);string target=InstalledPath(id,p.Hash);Directory.CreateDirectory(Path.GetDirectoryName(target));
     if(!File.Exists(target)||Hash(target)!=p.Hash){string temp=target+".tmp";File.Copy(source,temp,true);Verify(p,temp);if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}
     if(item==null){item=new PluginInstall{Id=id};next.Items.Add(item);}item.Hash=p.Hash;item.Version=p.Version;item.Enabled=true;
    }else if(action=="remove"){next.Items.RemoveAll(x=>x!=null&&x.Id==id);}
    else if(action=="enable"||action=="disable"){if(item==null)throw new InvalidOperationException("请先安装插件。");if(action=="enable")Verify(p,InstalledPath(id,item.Hash));item.Enabled=action=="enable";}
    else throw new InvalidOperationException("未知操作。");
    return Persist(next);
   }catch(Exception ex){Status="未应用："+ex.Message;return false;}
  }
  void CleanRemovedFiles(){foreach(var p in Catalog){string folder=Path.Combine(root,"installed",p.Id);if(!Directory.Exists(folder))continue;foreach(string dir in Directory.GetDirectories(folder)){string hash=Path.GetFileName(dir);if(!ValidHash(hash))continue;var item=Find(p.Id);if(item!=null&&item.Hash==hash)continue;try{File.Delete(Path.Combine(dir,"module.dll"));File.Delete(Path.Combine(dir,"module.dll.tmp"));if(Directory.GetFileSystemEntries(dir).Length==0)Directory.Delete(dir);}catch{Status="部分旧插件文件将在下次启动清理。";}}}}
  public PluginPage Open(string id,PetWindow pet,Action<PanelTab> navigate){
   if(!Enabled(id))throw new InvalidOperationException("请先在插件管理中安装并启用此功能。");
   try{var p=Package(id);Verify(p,paths[id]);var type=Assembly.LoadFrom(paths[id]).GetType("ElymsPet.Plugins."+id+".Entry",true);var plugin=Activator.CreateInstance(type) as IOfficialPlugin;if(plugin==null)throw new InvalidDataException("插件入口不兼容。");return plugin.Create(pet,navigate);}
   catch(Exception ex){active.Remove(id);errors[id]="加载失败，请修复插件："+(ex.InnerException??ex).Message;throw new InvalidOperationException(errors[id]);}
  }
 }
}
