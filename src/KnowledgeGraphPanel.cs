using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
namespace ElymsPet {
 public sealed class KnowledgeGraphPanel:IDisposable {
  readonly ArchiveService archive;
  readonly Action<string> interview;
  readonly Canvas canvas=new Canvas{Background=Theme.Brush("#FBF8F4")};
  readonly ScrollViewer viewport=new ScrollViewer{Height=460,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  readonly ScaleTransform scale=new ScaleTransform(1,1);
  readonly ComboBox domains=Combo(150),boundaries=Combo(135),boundary=Combo(130),level=Combo(110),target=Combo(250),kind=Combo(110);
  readonly TextBox search=Ui.Input("",36),title=Ui.Input("",36),domain=Ui.Input("",36),evidence=Ui.Input("",60),note=Ui.Input("",60),linkEvidence=Ui.Input("",60);
  readonly TextBlock stats=Ui.Text("",12,"#447969"),status=Ui.Text("",12,"#78616A"),selectedTitle=Ui.Text("选择节点查看与编辑",16,"#4F3B43"),zoomLabel=Ui.Text("100%",12,"#78616A");
  readonly CheckBox neighbors=new CheckBox{Content="只看所选节点及相邻知识",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8)};
  readonly StackPanel links=new StackPanel(),editor=new StackPanel();
  readonly DispatcherTimer searchTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
  string selected="",slotId="";
  int revision=-1,generation;
  bool refreshing,disposed,panning;
  Point panStart;double panX,panY;
  KnowledgeGraphScene scene;
  public UIElement Root {get;private set;}
  public bool IsLayoutReady {get;private set;}
  static ComboBox Combo(double width) {return new ComboBox{Width=width,MinHeight=34,Margin=new Thickness(0,0,8,8),VerticalContentAlignment=VerticalAlignment.Center};}
  sealed class NodeOption {public string Id,Text;public override string ToString(){return Text;}}
  public KnowledgeGraphPanel(ArchiveService archive,Action<string> interview) {
   this.archive=archive;this.interview=interview;
   search.Width=200;search.MaxLength=100;search.ToolTip="搜索知识名称、领域或备注";
   boundaries.ItemsSource=new[]{"全部掌握状态","已掌握","正在学习","尚未掌握","待核实"};boundaries.SelectedIndex=0;
   boundary.ItemsSource=new[]{"已掌握","正在学习","尚未掌握","待核实"};boundary.SelectedIndex=3;
   level.ItemsSource=new[]{"了解","会用","熟练","专长"};level.SelectedIndex=0;
   kind.ItemsSource=KnowledgeGraph.Kinds;kind.SelectedIndex=0;
   title.MaxLength=40;domain.MaxLength=16;evidence.MaxLength=160;note.MaxLength=160;linkEvidence.MaxLength=500;
   evidence.AcceptsReturn=note.AcceptsReturn=linkEvidence.AcceptsReturn=true;
   canvas.LayoutTransform=scale;viewport.Content=canvas;
   var frame=new Border{BorderBrush=Theme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Child=viewport,ClipToBounds=true,Margin=new Thickness(0,8,0,8)};
   var controls=Ui.Row(domains,boundaries,search,Ui.Button("清除筛选",ClearFilters,false));
   var actions=Ui.Row(Ui.Button("−",delegate {Zoom(scale.ScaleX/1.2);},false),zoomLabel,Ui.Button("＋",delegate {Zoom(scale.ScaleX*1.2);},false),Ui.Button("适合窗口",Fit,false),neighbors,Ui.Button("导出 PNG",ExportPng,false),Ui.Button("导出图谱 JSON",ExportJson,false));
   editor.Children.Add(selectedTitle);
   editor.Children.Add(Ui.Text("知识点名称 / 所属领域",12,"#78616A"));
   var fields=new Grid();fields.ColumnDefinitions.Add(new ColumnDefinition());fields.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(180)});fields.Children.Add(title);Grid.SetColumn(domain,1);fields.Children.Add(domain);editor.Children.Add(fields);
   editor.Children.Add(Ui.Row(boundary,level));
   editor.Children.Add(Ui.Text("掌握依据（原话、实践经历或资料位置）",12,"#78616A"));editor.Children.Add(evidence);
   editor.Children.Add(Ui.Text("备注 / 不确定之处",12,"#78616A"));editor.Children.Add(note);
   editor.Children.Add(Ui.Row(Ui.Button("保存知识点",SaveNode,true),Ui.Button("围绕此知识深入访谈",delegate {var item=Selected();if(item!=null)interview(item.Domain+" / "+item.Title);},false),Ui.Button("删除知识点及关联",DeleteNode,false)));
   var relationEditor=Ui.Stack(Ui.Text("添加关系：当前节点 → 关系 → 目标节点",12,"#78616A"),Ui.Row(kind,target),Ui.Text("关系依据 / 为什么这样连接",12,"#78616A"),linkEvidence,Ui.Button("保存并确认关系",SaveLink,false));
   editor.Children.Add(new Expander{Header="建立知识之间的链接",Content=relationEditor,Margin=new Thickness(0,8,0,8)});
   editor.Children.Add(Ui.Text("所选节点的关系（点击图中连线也可定位）",13,"#4F3B43"));editor.Children.Add(links);
   Root=Ui.Stack(Ui.Text("你的知识如何连接",18,"#4F3B43"),Ui.Text("绿：已掌握 · 蓝：学习中 · 橙：尚未掌握 · 灰：待核实。实线为已确认关系，虚线为待核实；箭头按“起点 → 关系 → 终点”阅读。",12,"#78616A"),controls,stats,actions,frame,Ui.Row(Ui.Button("＋ 新增知识点",delegate {SelectNode("");},true),Ui.Text("点击节点查看依据 · Ctrl + 滚轮缩放 · 拖动空白平移",11,"#795F69")),status,Ui.Card(editor));
   domains.SelectionChanged+=delegate {if(!refreshing)Rebuild();};boundaries.SelectionChanged+=delegate {if(!refreshing)Rebuild();};
   neighbors.Checked+=delegate {if(!refreshing)Rebuild();};neighbors.Unchecked+=delegate {if(!refreshing)Rebuild();};
   search.TextChanged+=delegate {if(!refreshing){searchTimer.Stop();searchTimer.Start();}};searchTimer.Tick+=delegate {searchTimer.Stop();Rebuild();};
   viewport.PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e){if((Keyboard.Modifiers&ModifierKeys.Control)!=0){Zoom(scale.ScaleX*(e.Delta>0?1.15:1/1.15));e.Handled=true;}};
   canvas.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.OriginalSource!=canvas)return;panning=true;panStart=e.GetPosition(viewport);panX=viewport.HorizontalOffset;panY=viewport.VerticalOffset;viewport.CaptureMouse();e.Handled=true;};
   viewport.MouseMove+=delegate(object sender,MouseEventArgs e){if(!panning)return;var p=e.GetPosition(viewport);viewport.ScrollToHorizontalOffset(panX-(p.X-panStart.X));viewport.ScrollToVerticalOffset(panY-(p.Y-panStart.Y));};
   viewport.MouseLeftButtonUp+=delegate {panning=false;viewport.ReleaseMouseCapture();};viewport.LostMouseCapture+=delegate {panning=false;};
   archive.Changed+=Refresh;Refresh();
  }
  ArchiveItem Selected() {return archive.Slot.Items.Find(delegate(ArchiveItem i){return i.Id==selected;});}
  void Refresh() {
   if(disposed)return;status.Text=archive.Status;editor.IsEnabled=!archive.Busy;
   if(revision==archive.ProfileRevision&&slotId==archive.Slot.Id)return;
   bool changedSlot=slotId!=archive.Slot.Id;slotId=archive.Slot.Id;revision=archive.ProfileRevision;
   refreshing=true;string old=changedSlot?"全部领域":domains.SelectedItem as string;
   var list=new List<string>{"全部领域"};foreach(var item in archive.Slot.Items)if(!list.Contains(item.Domain))list.Add(item.Domain);
   domains.ItemsSource=list;domains.SelectedItem=list.Contains(old??"")?old:"全部领域";
   if(changedSlot){selected="";neighbors.IsChecked=false;search.Clear();boundaries.SelectedIndex=0;}
   refreshing=false;
   if(Selected()==null)selected="";
   PopulateEditor();Rebuild();
  }
  void ClearFilters() {refreshing=true;domains.SelectedIndex=0;boundaries.SelectedIndex=0;search.Clear();neighbors.IsChecked=false;refreshing=false;Rebuild();}
  void Rebuild() {
   if(disposed||refreshing)return;
   var slot=archive.Slot;
   var view=KnowledgeGraph.Filter(slot,domains.SelectedIndex<=0?"":(string)domains.SelectedItem,boundaries.SelectedIndex<=0?"":(string)boundaries.SelectedItem,search.Text,neighbors.IsChecked==true?selected:"",80);
   int pending=slot.Links.FindAll(delegate(KnowledgeLink e){return !e.Confirmed;}).Count;
   stats.Text="共 "+slot.Items.Count+" 个知识点 · "+slot.Links.Count+" 条关系（"+pending+" 条待核实） · 当前显示 "+view.Nodes.Count+" / "+view.MatchingCount+(view.MatchingCount>80?"，请筛选领域或关键词查看其余节点":"");
   int mastered=slot.Items.FindAll(delegate(ArchiveItem i){return i.Boundary=="已掌握";}).Count;
   int uncertain=slot.Items.FindAll(delegate(ArchiveItem i){return i.Boundary=="待核实"||i.Boundary=="尚未掌握";}).Count;
   stats.Text+="\n已掌握 "+mastered+" · 尚未掌握 / 待核实 "+uncertain+"（仅统计已记录知识，不代表领域覆盖率）";
   int request=++generation;IsLayoutReady=false;canvas.Children.Clear();scene=null;
   canvas.Width=640;canvas.Height=400;
   var waiting=Ui.Text(view.Nodes.Count==0?(slot.Items.Count==0?"还没有知识点。点击下方“新增知识点”，或通过访谈建立第一份知识图谱。":"没有匹配的知识点。试试清除筛选。"):"正在整理知识布局…",14,"#78616A");waiting.Width=500;Canvas.SetLeft(waiting,36);Canvas.SetTop(waiting,60);canvas.Children.Add(waiting);
   if(view.Nodes.Count==0){IsLayoutReady=true;return;}
   ThreadPool.QueueUserWorkItem(delegate {
    KnowledgeGraphScene calculated=null;string error="";try{calculated=KnowledgeGraphLayout.Calculate(view);}catch(Exception e){error=e.Message;}
    if(canvas.Dispatcher.HasShutdownStarted)return;
    canvas.Dispatcher.BeginInvoke(new Action(delegate {
     if(disposed||generation!=request)return;
     if(calculated==null){status.Text="图谱布局失败，可缩小筛选范围后重试："+error;IsLayoutReady=true;return;}
     scene=calculated;IsLayoutReady=true;Draw();Fit();
    }));
   });
  }
  static string NodeColor(string b) {return b=="已掌握"?"#DDEEE4":b=="正在学习"?"#E0EBFA":b=="尚未掌握"?"#FBE6CD":"#ECE8E5";}
  void Draw() {
   if(scene==null)return;canvas.Children.Clear();canvas.Width=scene.Width;canvas.Height=scene.Height;
   foreach(var edge in scene.Edges) {
    var stroke=Theme.Brush(edge.Link.Confirmed?"#7B8C87":"#B69C81");
    var poly=new Polyline{Stroke=stroke,StrokeThickness=2,Points=new PointCollection(edge.Points),ToolTip=KnowledgeGraph.Describe(archive.Slot,edge.Link)+"\n"+edge.Link.Evidence,Cursor=Cursors.Hand};
    if(!edge.Link.Confirmed)poly.StrokeDashArray=new DoubleCollection(new[]{4.0,3.0});
    string nodeId=edge.Link.From;poly.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){SelectNode(nodeId);e.Handled=true;};canvas.Children.Add(poly);
    if(edge.Points.Count>1&&edge.Link.Kind!="相关"&&edge.Link.Kind!="对比") {
     Point end=edge.Points[edge.Points.Count-1],before=edge.Points[edge.Points.Count-2];Vector v=end-before;if(v.Length>0){v.Normalize();Vector side=new Vector(-v.Y,v.X);canvas.Children.Add(new Polygon{Fill=stroke,Points=new PointCollection(new[]{end,end-v*11+side*5,end-v*11-side*5}),IsHitTestVisible=false});}
    }
    var label=new Border{Background=Theme.Brush("#FBF8F4"),Padding=new Thickness(4,1,4,1),CornerRadius=new CornerRadius(4),Child=new TextBlock{Text=edge.Link.Kind+(edge.Link.Confirmed?"":" · 待核实"),FontSize=11,Foreground=stroke},ToolTip=poly.ToolTip,Cursor=Cursors.Hand};
    label.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){SelectNode(nodeId);e.Handled=true;};Canvas.SetLeft(label,edge.Label.X-38);Canvas.SetTop(label,edge.Label.Y-11);canvas.Children.Add(label);
   }
   foreach(var node in scene.Nodes) {
    var i=node.Item;string id=i.Id;
    var content=new StackPanel();content.Children.Add(new TextBlock{Text=i.Title,FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=Theme.TextMain,TextTrimming=TextTrimming.CharacterEllipsis});content.Children.Add(new TextBlock{Text=i.Domain+" · "+i.Boundary+" / "+i.Level,FontSize=10,Foreground=Theme.TextSoft,Margin=new Thickness(0,7,0,0),TextTrimming=TextTrimming.CharacterEllipsis});
    var button=new Button{Content=content,Width=KnowledgeGraphLayout.NodeWidth,Height=KnowledgeGraphLayout.NodeHeight,Background=Theme.Brush(NodeColor(i.Boundary)),BorderBrush=id==selected?Theme.Accent:Theme.Brush("#C9D2CD"),BorderThickness=new Thickness(id==selected?3:1),Padding=new Thickness(12),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=ArchiveLearning.ItemText(i),Cursor=Cursors.Hand};
    button.Click+=delegate {SelectNode(id);};Canvas.SetLeft(button,node.X);Canvas.SetTop(button,node.Y);canvas.Children.Add(button);
   }
  }
  void SelectNode(string id) {selected=id;PopulateEditor();if(neighbors.IsChecked==true)Rebuild();else Draw();}
  void PopulateEditor() {
   var item=Selected();selectedTitle.Text=item==null?"新增知识点":"当前节点 · "+item.Title;
   title.Text=item==null?"":item.Title;domain.Text=item==null?(domains.SelectedIndex>0?(string)domains.SelectedItem:""):item.Domain;
   boundary.SelectedItem=item==null?"待核实":item.Boundary;level.SelectedItem=item==null?"了解":item.Level;evidence.Text=item==null?"":item.Evidence;note.Text=item==null?"":item.Note;
   var choices=new List<NodeOption>();foreach(var i in archive.Slot.Items)if(i.Id!=selected)choices.Add(new NodeOption{Id=i.Id,Text=i.Domain+" / "+i.Title});target.ItemsSource=choices;target.SelectedIndex=choices.Count>0?0:-1;linkEvidence.Clear();
   links.Children.Clear();if(item==null){links.Children.Add(Ui.Text("先选择或保存一个知识点，再建立关系。",12,"#795F69"));return;}
   foreach(var e in archive.Slot.Links)if(e.From==selected||e.To==selected) {
    var link=e;links.Children.Add(Ui.Card(Ui.Stack(Ui.Text(KnowledgeGraph.Describe(archive.Slot,link),13,"#4F3B43"),Ui.Text((link.Confirmed?"已确认":"待核实")+" · "+link.Source+"\n依据："+link.Evidence,12,"#78616A"),Ui.Row(Ui.Button(link.Confirmed?"退回待核实":"确认这条关系",delegate {archive.ConfirmKnowledgeLink(link);},false),Ui.Button("删除关系",delegate {archive.DeleteKnowledgeLink(link);},false)))));
   }
   if(links.Children.Count==0)links.Children.Add(Ui.Text("这是一个尚未关联的知识点。可以手动连接，也可以继续访谈说明它与其他知识的关系。",12,"#795F69"));
  }
  void SaveNode() {
   string name=ArchiveStore.Clean(title.Text),field=ArchiveStore.Clean(domain.Text);if(field.Length==0)field="未分类";
   if(archive.SaveKnowledgeNode(selected,title.Text,domain.Text,(string)boundary.SelectedItem,(string)level.SelectedItem,evidence.Text,note.Text)) {
    var item=archive.Slot.Items.Find(delegate(ArchiveItem i){return i.Title==name&&i.Domain==field;});if(item!=null)SelectNode(item.Id);
   }
  }
  void SaveLink() {var option=target.SelectedItem as NodeOption;if(option==null||Selected()==null){status.Text="请先选择起点与目标知识点。";return;}archive.SaveKnowledgeLink(selected,option.Id,(string)kind.SelectedItem,linkEvidence.Text);}
  void DeleteNode() {
   var item=Selected();if(item==null)return;
   var owner=Window.GetWindow(Root);string text="删除知识点「"+item.Title+"」及其所有关联？其他知识点会保留。";
   var result=owner==null?PopupDialog.Show(text,"删除知识点",MessageBoxButton.YesNo,MessageBoxImage.Warning):PopupDialog.Show(owner,text,"删除知识点",MessageBoxButton.YesNo,MessageBoxImage.Warning);
   if(result==MessageBoxResult.Yes)archive.DeleteItem(item);
  }
  void Zoom(double value) {value=Math.Max(.15,Math.Min(2.5,value));scale.ScaleX=scale.ScaleY=value;zoomLabel.Text=(value*100).ToString("0")+"%";}
  void Fit() {if(scene==null)return;double w=viewport.ViewportWidth>0?viewport.ViewportWidth:760;Zoom(Math.Min(1,Math.Min((w-24)/scene.Width,420/scene.Height)));viewport.ScrollToTop();viewport.ScrollToLeftEnd();}
  void ExportJson() {
   var dialog=new SaveFileDialog{Filter="知识图谱 JSON|*.json",FileName="知识图谱.json"};if(dialog.ShowDialog()!=true)return;
   string error;if(JsonFile.Save(dialog.FileName,KnowledgeGraph.Export(archive.Slot),System.IO.Path.GetDirectoryName(dialog.FileName),out error))status.Text="已导出完整图谱（含未确认关系）："+dialog.FileName;else status.Text="导出失败："+error;
  }
  void ExportPng() {
   if(scene==null){status.Text="请先显示至少一个知识点。";return;}
   var dialog=new SaveFileDialog{Filter="图谱图片 PNG|*.png",FileName="知识图谱.png"};if(dialog.ShowDialog()!=true)return;
   try {
    double ratio=Math.Min(1,Math.Min(4096/scene.Width,4096/scene.Height));var visual=new DrawingVisual();
    using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(canvas){Stretch=Stretch.Fill},null,new Rect(0,0,scene.Width*ratio,scene.Height*ratio));
    var bitmap=new RenderTargetBitmap(Math.Max(1,(int)(scene.Width*ratio)),Math.Max(1,(int)(scene.Height*ratio)),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(dialog.FileName))encoder.Save(file);
    status.Text="已导出当前筛选图谱（全量数据可导出 JSON）："+dialog.FileName;
   }catch(Exception e){status.Text="导出失败："+e.Message;}
  }
  public void Dispose() {disposed=true;generation++;searchTimer.Stop();archive.Changed-=Refresh;}
 }
}
