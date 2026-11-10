using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace ElymsPet {
 static class PopupRenderingTests {
  static int checks,opened;
  static readonly List<Exception> dispatcherErrors=new List<Exception>();
  static void Check(bool pass,string name){if(!pass)throw new Exception("Popup rendering: "+name);checks++;Console.WriteLine("PASS popup: "+name);}
  static void Pump() {
   var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate {frame.Continue=false;}));Dispatcher.PushFrame(frame);
   if(dispatcherErrors.Count>0)throw new Exception("Deferred popup failure",dispatcherErrors[0]);
  }
  static List<T> Visuals<T>(DependencyObject root) where T:DependencyObject {
   var result=new List<T>();if(root is T)result.Add((T)root);
   for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)result.AddRange(Visuals<T>(VisualTreeHelper.GetChild(root,i)));
   return result;
  }
  static ToolTip OpenTip(object content,UIElement target) {
   var tip=new ToolTip{Content=content,PlacementTarget=target,Placement=PlacementMode.Bottom,StaysOpen=true};tip.Opened+=delegate {opened++;};
   try {tip.IsOpen=true;tip.UpdateLayout();Pump();return tip;}catch{try{tip.IsOpen=false;}catch{}throw;}
  }
  static void Close(ToolTip tip){tip.IsOpen=false;Pump();}
  static void Snapshot(FrameworkElement visual,string name) {
   visual.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(visual.ActualWidth)),Math.Max(1,(int)Math.Ceiling(visual.ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
   var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("tests/popup-preview");using(var file=File.Create("tests/popup-preview/"+name+".png"))png.Save(file);
  }
  public static void Run(string root) {
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"popup-isolation");
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){dispatcherErrors.Add(e.Exception);e.Handled=true;};
   MenuTheme.Install(app);
   var button=new Button{Content="Hover target",Width=150,Height=40};var host=new Window{Content=button,Width=500,Height=200,ShowInTaskbar=false,ShowActivated=false};
   PetWindow pet=null;ControlWindow panel=null;ChatBarWindow bar=null;KnowledgeGraphPanel graph=null;
   var report=new StringBuilder();
   try {
    host.Show();host.UpdateLayout();
    // This intentionally opens a real Popup: parsing the style alone misses deferred template failures.
    var tip=OpenTip("关闭",button);
    Check(tip.IsOpen&&tip.ActualWidth>0&&PresentationSource.FromVisual(tip)!=null,"short tooltip creates a real native popup");Close(tip);
    string longText=string.Concat(new string[1]{new string('知',500)});
    tip=OpenTip(longText,button);
    var texts=Visuals<TextBlock>(tip);bool wraps=texts.Exists(delegate(TextBlock x){return x.Text==longText&&x.TextWrapping==TextWrapping.Wrap;});
    Check(wraps&&tip.ActualWidth<=361&&tip.ActualHeight>60,"long Chinese text wraps within 360 pixels");Snapshot(tip,"chinese-wrap");Close(tip);
    tip=OpenTip("https://example.org/"+new string('x',600),button);Check(tip.ActualWidth<=361&&tip.ActualHeight>40,"unbroken URLs wrap without expanding popup width");Close(tip);
    tip=OpenTip("第一行\n第二行\n第三行 · 中文 / emoji 🌱",button);Check(Visuals<TextBlock>(tip).Exists(delegate(TextBlock x){return x.Text.Contains("第二行");})&&tip.ActualHeight>45,"multiline Unicode tooltip preserves content");Snapshot(tip,"multiline");
    tip.Content="运行中更新提示";tip.UpdateLayout();Pump();Check(Visuals<TextBlock>(tip).Exists(delegate(TextBlock x){return x.Text=="运行中更新提示";}),"open tooltip content can change");Close(tip);
    foreach(object empty in new object[]{"",null}){tip=OpenTip(empty,button);Check(tip.ActualWidth<=361,"empty or null content opens safely");Close(tip);}
    var rich=new Border{Child=new TextBlock{Text="富内容提示",Foreground=Brushes.DarkGreen}};tip=OpenTip(rich,button);Check(Visuals<Border>(tip).Contains(rich),"custom visual tooltip content remains supported");Close(tip);
    int before=opened;for(int i=0;i<40;i++){tip=OpenTip("切换提示 "+i,button);Close(tip);}Check(opened-before==40,"40 repeated open/close cycles without deferred exceptions");
    var menu=MenuTheme.Create(true);var submenu=MenuTheme.Item("展开", "",null);submenu.Items.Add(MenuTheme.Item("子菜单项","",null));menu.Items.Add(submenu);menu.PlacementTarget=button;menu.IsOpen=true;Pump();menu.UpdateLayout();submenu.IsSubmenuOpen=true;Pump();Check(menu.IsOpen&&submenu.IsSubmenuOpen,"branded context menu and submenu render");submenu.IsSubmenuOpen=false;menu.IsOpen=false;Pump();
    var edit=MenuTheme.EditMenu(new TextBox{Text="copy"});edit.PlacementTarget=button;edit.IsOpen=true;Pump();Check(edit.ActualWidth>0,"text editing context menu renders");edit.IsOpen=false;Pump();
    var promptType=typeof(PopupDialog).Assembly.GetType("ElymsPet.PromptWindow");var prompt=(Window)Activator.CreateInstance(promptType,new object[]{"弹窗测试，不操作用户资料。","测试",MessageBoxButton.YesNo,MessageBoxImage.Information});
    prompt.ShowActivated=false;prompt.Show();prompt.UpdateLayout();var closeButton=Visuals<Button>(prompt).Find(delegate(Button b){return (string)b.ToolTip=="关闭";});
    Check(closeButton!=null,"confirmation dialog exposes close tooltip");tip=OpenTip(closeButton.ToolTip,closeButton);Close(tip);prompt.Close();Pump();
    pet=new PetWindow(new PetState(),true);panel=new ControlWindow(pet);panel.ShowActivated=false;panel.Show();
    var samples=new HashSet<string>();int pageCount=0;
    for(int i=0;i<panel.Tabs.Items.Count;i++) {
     panel.Tabs.SelectedIndex=i;panel.UpdateLayout();Pump();CollectTips(panel,samples);pageCount++;
     foreach(var nested in Visuals<TabControl>(panel))if(nested!=panel.Tabs)for(int j=0;j<nested.Items.Count;j++){nested.SelectedIndex=j;panel.UpdateLayout();Pump();CollectTips(panel,samples);}
    }
    Check(pageCount==10,"all 10 control-panel pages instantiate with installed theme");
    bar=new ChatBarWindow(pet);bar.ShowBar();bar.UpdateLayout();CollectTips(bar,samples);bar.ShowArchive();bar.UpdateLayout();Pump();CollectTips(bar,samples);
    Check(samples.Count>=15,"collect real tooltip content from panel and floating chat/archive modes");
    foreach(string sample in samples){tip=OpenTip(sample,button);Check(tip.ActualWidth<=361,"real app tooltip fits: "+ChatText.Clip(sample,24));Close(tip);}
    var slot=pet.Archive.Slot;var a=slot.Items[0];var secondNode=slot.Items[1];pet.Archive.SaveKnowledgeLink(a.Id,secondNode.Id,"相关","图谱悬停测试依据，仅使用预览数据");
    graph=new KnowledgeGraphPanel(pet.Archive,delegate {});host.Content=graph.Root;host.Width=900;host.Height=900;
    var deadline=DateTime.UtcNow.AddSeconds(15);while(!graph.IsLayoutReady&&DateTime.UtcNow<deadline){Pump();System.Threading.Thread.Sleep(10);}host.UpdateLayout();Pump();
    Check(graph.IsLayoutReady,"graph asynchronous layout completes with themed tooltips");
    var graphTips=new HashSet<string>();CollectTips(host,graphTips);Check(graphTips.ExistsText("图谱悬停测试依据"),"graph edge and node tooltip contents are available");
    foreach(string sample in graphTips){tip=OpenTip(sample,button);Close(tip);}Check(true,"graph node/edge tooltip popups render");
    // Close while a new graph layout is pending; late completion must not touch disposed controls.
    pet.Archive.SaveKnowledgeNode("","关闭前新增节点","测试","待核实","了解","测试","");graph.Dispose();host.Content=button;graph=null;
    for(int i=0;i<20;i++){Pump();System.Threading.Thread.Sleep(10);}Check(dispatcherErrors.Count==0,"closing graph during layout produces no deferred UI exception");
    Check(!Directory.Exists(Storage.Folder)||Directory.GetFiles(Storage.Folder,"*",SearchOption.AllDirectories).Length==0,"UI tests do not write user settings or archive data");
    report.AppendLine("PASS popup checks="+checks+" real popups opened="+opened+" distinct application hints="+samples.Count);
   }catch(Exception e){report.AppendLine("FAIL "+e);throw;}
   finally {
    if(graph!=null)graph.Dispose();if(bar!=null)bar.Close();if(panel!=null)panel.Close();if(pet!=null)pet.Close();host.Close();
    Directory.CreateDirectory("tests");File.WriteAllText("tests/popup-tests.txt",report.ToString());Storage.Folder=previous;
    app.Shutdown();
   }
   Console.WriteLine(report.ToString());
  }
  static void CollectTips(DependencyObject root,HashSet<string> samples){foreach(var e in Visuals<FrameworkElement>(root)){string text=e.ToolTip as string;if(!string.IsNullOrEmpty(text))samples.Add(text);}}
  static bool ExistsText(this HashSet<string> values,string value){foreach(var text in values)if(text.Contains(value))return true;return false;}
 }
}
