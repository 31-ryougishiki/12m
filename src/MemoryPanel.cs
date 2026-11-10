using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ElymsPet {
 /// <summary>「记忆」页：她记住的事、待确认的推断，以及忘记；存在本机，可以随时删。</summary>
 public sealed class MemoryPanel : IDisposable {
  readonly PetWindow pet;
  readonly MemoryService memory;
  StackPanel list;
  TextBlock status;
  public UIElement Root { get; private set; }
  public MemoryPanel(PetWindow target) {
   pet = target; memory = pet.Memory;
   Root = Build();
   memory.Changed += Refresh;
   Refresh();
  }
  public void Dispose() { memory.Changed -= Refresh; }
  Window Owner { get { return pet.Panel; } }
  MessageBoxResult Ask(string text, string title) {
   return Owner == null ? PopupDialog.Show(text, title) : PopupDialog.Show(Owner, text, title);
  }
  MessageBoxResult Ask(string text, string title, MessageBoxButton buttons, MessageBoxImage icon) {
   return Owner == null ? PopupDialog.Show(text, title, buttons, icon) : PopupDialog.Show(Owner, text, title, buttons, icon);
  }
  UIElement Build() {
   var tab = new StackPanel();
   status = Ui.Text("", 12, "#78616A");
   var head = Ui.Stack(
    Ui.Text("记忆 · 她记得的事", 18, "#4F3B43"),
    Ui.Text("聊天里值得长期记住的事，她会自己记下来；重启之后还在。全部保存在本机数据文件夹，随时可以删。", 12, "#78616A"),
    status,
    Ui.Row(Ui.Button("打开数据文件夹", OpenFolder, false), Ui.Button("忘记全部", ClearAll, false)),
    Ui.Text("只有你说过的话才算数；她自己猜的会标成「待确认」，你确认之后才会在聊天里用到。", 11, "#795F69"));
   tab.Children.Add(Ui.Card(head));
   list = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
   var body = Ui.Stack(
    Ui.Text("全部记忆", 18, "#4F3B43"),
    Ui.Text("越靠前是越近提到的。点「忘记」会彻底删掉这一条，同样的内容也不会再被记回来。", 12, "#78616A"),
    list);
   tab.Children.Add(Ui.Card(body));
   return tab;
  }
  void OpenFolder() {
   try { Directory.CreateDirectory(Storage.Folder); Process.Start("explorer.exe", Storage.Folder); }
   catch (Exception e) { PopupDialog.Show(Owner, "打不开数据文件夹：" + e.Message, "记忆"); }
  }
  void ClearAll() {
   if (memory.Count == 0) return;
   if (Ask("忘记全部 " + memory.Count + " 条记忆？\n删除之后她就不记得这些事了，这个操作不能撤销。", "忘记全部", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
   memory.Clear();
  }
  void Refresh() {
   if (list == null) return;
   list.Children.Clear();
   MemoryItem[] items = memory.Items;
   string note = MemoryStore.Stats(memory.Book);
   if (memory.Book != null && memory.Book.Updated != DateTime.MinValue) note += " · 更新 " + TimeText.Local(memory.Book.Updated);
   if (memory.LastError.Length > 0) note += " · 上次保存出错：" + memory.LastError;
   status.Text = note;
   if (items.Length == 0) {
    list.Children.Add(Ui.Text("还没有记忆。多聊几次，值得记住的事会出现在这里。", 12, "#795F69"));
    return;
   }
   int shown = 0;
   for (int i = 0; i < items.Length && shown < 80; i++) {
    if (items[i] == null) continue;
    list.Children.Add(Row(items[i]));
    shown++;
   }
   if (items.Length > shown) list.Children.Add(Ui.Text("还有 " + (items.Length - shown) + " 条更早的记忆，没有全部显示。", 11, "#795F69"));
  }
  UIElement Row(MemoryItem item) {
   string head = item.Kind + " · " + TimeText.Local(item.LastSeen) + " · 提到 " + item.Hits + " 次";
   if (item.Core) head += " · 核心";
   if (item.Pending) head += " · 待确认";
   var top = Ui.Text(head, 11, item.Pending ? "#8F642D" : "#795F69");
   top.Margin = new Thickness(0, 0, 0, 4);
   var text = Ui.Text(item.Text, 13.5, "#59434B");
   text.Margin = new Thickness(0, 0, 0, 6);
   var buttons = new List<UIElement>();
   if (item.Pending) {
    string id = item.Id;
    buttons.Add(Ui.Button("确认这条", delegate { memory.Confirm(id); }, true));
   }
   string target = item.Id;
   buttons.Add(Ui.Button("忘记", delegate { memory.Forget(target); }, false));
   var stack = Ui.Stack(top, text, Ui.Row(buttons.ToArray()));
   return new Border {
    Background = Ui.Brush("#FFF5EE"), BorderBrush = Ui.Brush("#E9D6CF"), BorderThickness = new Thickness(1),
    CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 11, 14, 8), Margin = new Thickness(0, 0, 0, 9), Child = stack
   };
  }
 }
}
