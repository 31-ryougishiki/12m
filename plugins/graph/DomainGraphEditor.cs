using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
namespace ElymsPet {
 public static class DomainGraphEditor {
  public static string Prompt(Window owner,string topic,string draft=""){var window=Window(owner,"让曳尔沐编辑图谱");window.Height=460;var input=Input(draft,130,2000);string result=null;window.Content=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new Border{Padding=new Thickness(24),Child=Ui.Stack(Ui.Text(topic,20,"#4F3B43"),Ui.Text("可以新增或调整子模块、移动节点，也可以新增节点、修改节点与关系。例如：在当前模块建立“输入准备”子模块，将相关已有节点移入，保留其他节点与关系。",14,"#78616A"),Ui.Text("未指定归属时，新节点放在当前模块。请提供已知源码或资料；缺少依据的内容会标为待核实。保存后可直接查看新节点。",13,"#78616A"),input,Ui.Row(Ui.Button("整理并保存",delegate{if(string.IsNullOrWhiteSpace(input.Text))return;result=input.Text.Trim();window.DialogResult=true;},true),Ui.Button("取消",delegate{window.Close();},false)))}};window.ShowDialog();return result;}
  static Window Window(Window owner,string title){var window=new Window{Owner=owner,Title=title,Width=760,Height=800,MaxHeight=SystemParameters.WorkArea.Height-40,MinWidth=600,Background=Theme.Canvas,FontFamily=Theme.Font,WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner};GraphCopyText.Enable(window);return window;}
  static TextBox Input(string text,int height,int limit){var b=Ui.Input(text,height);b.MaxLength=limit;b.AcceptsReturn=height>44;b.VerticalContentAlignment=VerticalAlignment.Top;b.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;return b;}
  public static DomainGraph NewDomain(Window owner){var window=Window(owner,"新建领域图谱");window.Height=300;var title=Input("",44,80);DomainGraph result=null;var error=Ui.Text("",13,"#AD506B");
   window.Content=new Border{Padding=new Thickness(24),Child=Ui.Stack(Ui.Text("为一个具体领域搭建图谱",22,"#4F3B43"),Ui.Text("例如：vLLM-Ascend 性能调优、模型部署与验收",14,"#78616A"),title,error,Ui.Row(Ui.Button("创建领域",delegate{if(string.IsNullOrWhiteSpace(title.Text)){error.Text="请填写领域名称。";return;}result=new DomainGraph{Id=Guid.NewGuid().ToString("N"),Title=title.Text.Trim(),Description="专属领域档案 · 从关键知识开始连接"};window.DialogResult=true;},true),Ui.Button("取消",delegate{window.Close();},false)))};window.ShowDialog();return result;
  }
  public static string Edit(Window owner,DomainGraph graph,string id,Func<DomainGraph,string> persist=null){
   var draft=DomainKnowledge.Clone(graph);var node=id==null?new DomainNode{Id=Guid.NewGuid().ToString("N"),Status="个人整理 · 待核实"}:draft.Nodes.Find(delegate(DomainNode n){return n.Id==id;});if(node==null)return null;
   var window=Window(owner,id==null?"新增知识节点":"编辑知识节点");string result=null;
   TextBox title=Input(node.Title,44,120),summary=Input(node.Summary,90,6000),approach=Input(node.Approach,140,20000),code=Input(node.Code,180,40000),sources=Input(node.Sources,90,6000),version=Input(node.Version,44,200),state=Input(node.Status,44,200);
   code.FontFamily=new System.Windows.Media.FontFamily("Consolas");
   var tier=new ComboBox{MinHeight=36,ItemsSource=DomainKnowledge.Tiers,SelectedItem=node.Tier,Margin=new Thickness(0,0,0,12)};
   var targets=new ComboBox{MinHeight=36,Width=270,DisplayMemberPath="Title",ItemsSource=graph.Nodes.FindAll(delegate(DomainNode n){return n.Id!=node.Id;}),Margin=new Thickness(0,0,8,8)}; // fields require explicit display wrapper below
   var options=new List<NodeOption>();foreach(var n in graph.Nodes)if(n.Id!=node.Id)options.Add(new NodeOption{Id=n.Id,Text=n.Title});targets.DisplayMemberPath="";targets.ItemsSource=options;
   var relation=Input("依赖",38,60);relation.Width=130;var relations=new StackPanel();var related=new List<DomainEdge>();foreach(var e in graph.Edges)if(e.From==node.Id||e.To==node.Id)related.Add(e);
   var direction=new ComboBox{MinHeight=36,Width=180,ItemsSource=new[]{"当前节点 → 对方","对方 → 当前节点"},SelectedIndex=0,Margin=new Thickness(0,0,8,8)};
   Action redraw=null;redraw=delegate{relations.Children.Clear();foreach(var e in related){var edge=e;bool outgoing=edge.From==node.Id;string otherId=outgoing?edge.To:edge.From;var target=graph.Nodes.Find(delegate(DomainNode n){return n.Id==otherId;});string other=target==null?otherId:target.Title;var row=new DockPanel{Margin=new Thickness(0,4,0,4)};var remove=ModernControls.Icon("×","移除关联",delegate{related.Remove(edge);redraw();});DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);row.Children.Add(Ui.Text((outgoing?"当前节点":other)+" — "+edge.Label+" → "+(outgoing?other:"当前节点"),14,"#4F3B43"));relations.Children.Add(row);}};redraw();
   var error=Ui.Text("",13,"#AD506B");
   var content=Ui.Stack(Ui.Text("节点内容",24,"#4F3B43"),Ui.Text("名称",13,"#78616A"),title,Ui.Text("等级：CORE 核心高频 / S 复杂特性 / A 基础 / B 辅助或历史",13,"#78616A"),tier,Ui.Text("介绍",13,"#78616A"),summary,Ui.Text("实现思路 / 使用条件",13,"#78616A"),approach,Ui.Text("代码 / 命令（只保存文本，不执行）",13,"#78616A"),code,Ui.Text("来源链接（每行一个）",13,"#78616A"),sources,Ui.Text("适用版本",13,"#78616A"),version,Ui.Text("状态，例如：已验证、低频工具、历史弃用",13,"#78616A"),state,new Expander{Header="与其他节点的关系",IsExpanded=true,Content=Ui.Stack(relations,Ui.Row(direction,targets),Ui.Row(relation,Ui.Button("添加关联",delegate{var target=targets.SelectedItem as NodeOption;if(target==null||string.IsNullOrWhiteSpace(relation.Text)){error.Text="请选择关联节点并填写关系。";return;}string from=direction.SelectedIndex==0?node.Id:target.Id,to=direction.SelectedIndex==0?target.Id:node.Id;if(!related.Exists(delegate(DomainEdge e){return e.From==from&&e.To==to&&e.Label==relation.Text.Trim();}))related.Add(new DomainEdge{From=from,To=to,Label=relation.Text.Trim(),Evidence="个人整理"});error.Text="";redraw();},false)))});
   var footer=Ui.Stack(error,Ui.Row(Ui.Button("保存节点",delegate{if(string.IsNullOrWhiteSpace(title.Text)||string.IsNullOrWhiteSpace(summary.Text)){error.Text="请填写名称和介绍，让节点有实际内容。";return;}
    node.Title=title.Text.Trim();node.Tier=(string)tier.SelectedItem;node.Summary=summary.Text;node.Approach=approach.Text;node.Code=code.Text;node.Sources=sources.Text;node.Version=version.Text;node.Status=state.Text;
    var candidate=DomainKnowledge.Clone(graph);int index=candidate.Nodes.FindIndex(delegate(DomainNode n){return n.Id==node.Id;});if(index<0)candidate.Nodes.Add(node);else candidate.Nodes[index]=node;candidate.Edges.RemoveAll(delegate(DomainEdge e){return e.From==node.Id||e.To==node.Id;});candidate.Edges.AddRange(related);
    if(persist!=null){try{string failure=persist(candidate);if(!string.IsNullOrEmpty(failure)){error.Text=failure;return;}}catch(Exception ex){error.Text="保存失败，编辑内容已保留："+ex.Message;return;}}
    graph.Nodes=candidate.Nodes;graph.Edges=candidate.Edges;result=node.Id;window.DialogResult=true;
   },true),Ui.Button("取消",delegate{window.Close();},false)));
   var layout=new DockPanel{Margin=new Thickness(24)};DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);layout.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});window.Content=layout;window.ShowDialog();return result;
  }
  sealed class NodeOption {public string Id,Text;public override string ToString(){return Text;}}
 }
}

