using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace ElymsPet {
 /// <summary>记忆的读写、合并、检索和遗忘曲线；纯本地计算，不依赖网络。</summary>
 public static class MemoryStore {
  public const int MaxItems = 300;
  public const int MaxPerReply = 3;
  public const int MaxTextLength = 200;
  public static readonly string[] Kinds = { "事实", "偏好", "习惯", "事件", "承诺", "关系" };
  public static string LastError = "";
  public static string FilePath { get { return Path.Combine(Storage.Folder, "memories.json"); } }
  public static bool Exists { get { return File.Exists(FilePath); } }

  public static MemoryBook NewBook() {
   return new MemoryBook { Items = new MemoryItem[0], Blocked = new string[0] };
  }
  public static MemoryBook Load() {
   MemoryBook book = JsonFile.Load<MemoryBook>(FilePath, out LastError);
   return Prepare(book);
  }
  public static MemoryBook Prepare(MemoryBook book) {
   if (book == null) return NewBook();
   if (book.Items == null) book.Items = new MemoryItem[0];
   if (book.Blocked == null) book.Blocked = new string[0];
   var kept = new List<MemoryItem>();
   for (int i = 0; i < book.Items.Length; i++) {
    MemoryItem item = Normalize(book.Items[i]);
    if (item != null) kept.Add(item);
   }
   book.Items = kept.ToArray();
   if (book.Items.Length > MaxItems) {
    var list = new List<MemoryItem>(book.Items);
    Trim(list, DateTime.UtcNow);
    book.Items = list.ToArray();
   }
   return book;
  }
  public static bool Save(MemoryBook book) {
   if (book == null) return false;
   book.Updated = DateTime.UtcNow;
   return JsonFile.Save(FilePath, book, Storage.Folder, out LastError);
  }

  /// <summary>清洗一条记忆：去掉空条目、未知种类和过长文本。</summary>
  public static MemoryItem Normalize(MemoryItem item) {
   if (item == null) return null;
   item.Text = Clean(item.Text);
   if (item.Text.Length == 0) return null;
   if (item.Text.Length > MaxTextLength) item.Text = item.Text.Substring(0, MaxTextLength) + "…";
   if (!IsKind(item.Kind)) item.Kind = "事实";
   item.Entity = Clip(Clean(item.Entity), 40);
   item.Emotion = Clip(Clean(item.Emotion), 30);
   if (item.Source != "推断") item.Source = "明说";
   if (double.IsNaN(item.Importance) || double.IsInfinity(item.Importance)) item.Importance = DefaultImportance(item.Kind);
   item.Importance = Math.Max(0, Math.Min(1, item.Importance));
   if (item.Hits < 1) item.Hits = 1;
   if (item.Id == null || item.Id.Length == 0) item.Id = NewId();
   if (item.Created == DateTime.MinValue) item.Created = DateTime.UtcNow;
   if (item.LastSeen == DateTime.MinValue) item.LastSeen = item.Created;
   return item;
  }
  public static bool IsKind(string kind) {
   for (int i = 0; i < Kinds.Length; i++) if (Kinds[i] == kind) return true;
   return false;
  }
  static double DefaultImportance(string kind) {
   if (kind == "承诺" || kind == "关系") return 0.8;
   if (kind == "偏好" || kind == "习惯") return 0.6;
   if (kind == "事件") return 0.5;
   return 0.45;
  }
  public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

  /// <summary>写入一批记忆；返回新增条数。重复的内容只增加出现次数。</summary>
  public static int Add(MemoryBook book, MemoryItem[] items) { return Add(book, items, DateTime.UtcNow); }
  public static int Add(MemoryBook book, MemoryItem[] items, DateTime now) {
   return Apply(book, items, now).Added;
  }
  public static MemoryMutation Apply(MemoryBook book, MemoryItem[] items, DateTime now) {
   var result = new MemoryMutation();
   if (book == null || items == null || items.Length == 0) return result;
   var list = new List<MemoryItem>();
   if (book.Items != null) for (int i = 0; i < book.Items.Length; i++) if (book.Items[i] != null) list.Add(book.Items[i]);
   for (int i = 0; i < items.Length; i++) {
    MemoryItem item = Normalize(items[i]);
    if (item == null || IsBlocked(book, Fingerprint(item.Text))) { result.Rejected++; continue; }
    int index = IndexOf(list, item);
    if (index >= 0) { Merge(list[index], item, now); result.Updated++; }
    else {
     item.Id = NewId();
     item.Created = now;
     item.LastSeen = now;
     item.Hits = 1;
     list.Add(item);
     result.Added++;
    }
   }
   if (result.Changed) {
    Trim(list, now);
    book.Items = list.ToArray();
    book.Updated = now;
   }
   return result;
  }
  static int IndexOf(List<MemoryItem> list, MemoryItem item) {
   string fp = Fingerprint(item.Text);
   for (int i = 0; i < list.Count; i++) {
    MemoryItem other = list[i];
    if (other == null) continue;
    if (Fingerprint(other.Text) == fp) return i;
    if (item.Entity.Length > 0 && item.Entity == other.Entity && item.Kind == other.Kind && item.Text == other.Text) return i;
   }
   for (int i = 0; i < list.Count; i++) {
    MemoryItem other = list[i];
    if (other == null || other.Kind != item.Kind) continue;
    if (Similarity(other.Text, item.Text) >= 0.72) return i;
   }
   return -1;
  }
  static void Merge(MemoryItem target, MemoryItem item, DateTime now) {
   target.Hits++;
   target.LastSeen = now;
   target.Importance = Math.Max(target.Importance, item.Importance);
   if (item.Text.Length > target.Text.Length) target.Text = item.Text;
   if (item.Entity.Length > 0) target.Entity = item.Entity;
   if (item.Emotion.Length > 0) target.Emotion = item.Emotion;
   if (item.Source == "明说") { target.Source = "明说"; target.Pending = false; }
   if (item.Core) target.Core = true;
   if (item.Due.HasValue) target.Due = item.Due;
  }

  /// <summary>超过上限时，先丢掉最弱、又不属于核心的旧记忆。</summary>
  public static void Trim(List<MemoryItem> list, DateTime now) {
   while (list.Count > MaxItems) {
    int weakest = -1;
    double lowest = double.MaxValue;
    for (int i = 0; i < list.Count; i++) {
     MemoryItem item = list[i];
     if (item == null) { weakest = i; break; }
     if (item.Core || item.Pending) continue;
     double weight = Activation(item, now) * 0.6 + item.Importance * 0.4;
     if (weight < lowest) { lowest = weight; weakest = i; }
    }
    if (weakest < 0) break;
    list.RemoveAt(weakest);
   }
  }

  /// <summary>ACT-R 风格的基激活：出现得多就稳，隔得久就淡。</summary>
  public static double Activation(MemoryItem item, DateTime now) {
   if (item == null) return 0;
   if (item.Core) return 1;
   double age = (now - item.LastSeen).TotalDays;
   if (age < 0.25) age = 0.25;
   double hits = Math.Max(1, item.Hits);
   double baseLevel = Math.Log(1.0 + hits / Math.Sqrt(age));
   return 1.0 / (1.0 + Math.Exp(-(baseLevel - 0.7) * 2.2));
  }

  public static MemoryItem[] Search(MemoryBook book,string query,DateTime now,int top) {
   return MemoryRetrieval.Search(book,query,now,top);
  }

  public static MemoryItem[] Sorted(MemoryBook book) {
   var list = new List<MemoryItem>();
   if (book != null && book.Items != null) for (int i = 0; i < book.Items.Length; i++) if (book.Items[i] != null) list.Add(book.Items[i]);
   list.Sort(delegate(MemoryItem a, MemoryItem b) { return b.LastSeen.CompareTo(a.LastSeen); });
   return list.ToArray();
  }
  public static MemoryItem Find(MemoryBook book, string id) {
   if (book == null || book.Items == null || string.IsNullOrEmpty(id)) return null;
   for (int i = 0; i < book.Items.Length; i++) if (book.Items[i] != null && book.Items[i].Id == id) return book.Items[i];
   return null;
  }
  public static bool Confirm(MemoryBook book, string id) {
   MemoryItem item = Find(book, id);
   if (item == null) return false;
   item.Pending = false;
   item.Source = "明说";
   item.LastSeen = DateTime.UtcNow;
   item.Hits++;
   return true;
  }
  /// <summary>删除一条记忆，并记下指纹，避免同样的内容又被写回来。</summary>
  public static bool Forget(MemoryBook book, string id) {
   MemoryItem item = Find(book, id);
   if (item == null) return false;
   var list = new List<MemoryItem>();
   for (int i = 0; i < book.Items.Length; i++) if (book.Items[i] != null && book.Items[i].Id != id) list.Add(book.Items[i]);
   book.Items = list.ToArray();
   var blocked = new List<string>();
   if (book.Blocked != null) blocked.AddRange(book.Blocked);
   string fp = Fingerprint(item.Text);
   bool exists = false;
   for (int i = 0; i < blocked.Count; i++) if (blocked[i] == fp) { exists = true; break; }
   if (!exists) blocked.Add(fp);
   while (blocked.Count > 200) blocked.RemoveAt(0);
   book.Blocked = blocked.ToArray();
   return true;
  }
  public static bool IsBlocked(MemoryBook book, string fingerprint) {
   if (book == null || book.Blocked == null) return false;
   for (int i = 0; i < book.Blocked.Length; i++) if (book.Blocked[i] == fingerprint) return true;
   return false;
  }
  public static void Clear(MemoryBook book) {
   if (book == null) return;
   book.Items = new MemoryItem[0];
   book.Blocked = new string[0];
   book.Updated = DateTime.UtcNow;
  }

  public static string Stats(MemoryBook book) {
   if (book == null || book.Items == null || book.Items.Length == 0) return "还没有记忆";
   int pending = 0, core = 0;
   for (int i = 0; i < book.Items.Length; i++) {
    if (book.Items[i] == null) continue;
    if (book.Items[i].Pending) pending++;
    if (book.Items[i].Core) core++;
   }
   string text = book.Items.Length + " 条记忆";
   if (core > 0) text += " · 核心 " + core;
   if (pending > 0) text += " · 待确认 " + pending;
   return text;
  }

  /// <summary>检索结果拼成交给模型的上下文；以「记忆可用」开头时提示词才会附上记忆规则。</summary>
  public static string Recall(MemoryBook book, string query, bool enabled) {return Recall(book,query,enabled,DateTime.UtcNow);}
  public static string Recall(MemoryBook book, string query, bool enabled, DateTime now) {
   if (!enabled) return "记忆关闭了。";
   MemoryItem[] items = Search(book, query, now, 5);
   if (items.Length == 0) return "记忆可用。你们还没有存下值得记的事。";
   return "记忆可用。你记得的事：\n" + Prompt(book, items, now);
  }

  /// <summary>导出给人看的概要，聊天里只带最相关的一小段。</summary>
  public static string Prompt(MemoryBook book, MemoryItem[] items, DateTime now) {
   if (items == null || items.Length == 0) return "";
   var text = new StringBuilder();
   for (int i = 0; i < items.Length; i++) {
    MemoryItem item = items[i];
    string when = item.LastSeen.ToLocalTime().ToString("MM-dd");
    text.Append("- ").Append(when).Append(" ").Append(item.Kind).Append("｜").Append(Clip(item.Text, 80)).Append("\n");
   }
   string result = text.ToString().TrimEnd();
   if (result.Length > 560) result = result.Substring(0, 560) + "…";
   return result;
  }
  public static string Summarize(MemoryItem[] items) {
   if (items == null || items.Length == 0) return "";
   var text = new StringBuilder();
   for (int i = 0; i < items.Length; i++) {
    if (items[i] == null) continue;
    if (text.Length > 0) text.Append("；");
    text.Append(Clip(items[i].Text, 28));
   }
   return text.ToString();
  }

  /// <summary>预览用的示例记忆，不写磁盘。</summary>
  public static MemoryBook Demo() {
   var book = NewBook();
   DateTime now = DateTime.UtcNow;
   var list = new List<MemoryItem>();
   list.Add(new MemoryItem { Id = NewId(), Kind = "偏好", Text = "只喝冰美式，不加糖", Entity = "咖啡", Source = "明说", Importance = .6, Created = now.AddDays(-9), LastSeen = now.AddDays(-2), Hits = 3 });
   list.Add(new MemoryItem { Id = NewId(), Kind = "事件", Text = "上周把一个项目从头做到交付，熬了两个通宵", Source = "明说", Importance = .6, Created = now.AddDays(-6), LastSeen = now.AddDays(-6), Hits = 1 });
   list.Add(new MemoryItem { Id = NewId(), Kind = "关系", Text = "约定过，忙到很晚也要留一句晚安", Source = "明说", Importance = .9, Core = true, Created = now.AddDays(-30), LastSeen = now.AddDays(-1), Hits = 8 });
   list.Add(new MemoryItem { Id = NewId(), Kind = "偏好", Text = "好像不太喜欢下雨天", Source = "推断", Pending = true, Importance = .4, Created = now.AddDays(-1), LastSeen = now.AddDays(-1), Hits = 1 });
   book.Items = list.ToArray();
   return book;
  }

  public static string Clean(string text) {
   if (string.IsNullOrEmpty(text)) return "";
   var sb = new StringBuilder(text.Length);
   bool space = false;
   for (int i = 0; i < text.Length; i++) {
    char c = text[i];
    if (c == '\r' || c == '\n' || c == '\t') { space = true; continue; }
    if (char.IsControl(c)) continue;
    if (c == ' ') {
     if (!space && sb.Length > 0) sb.Append(' ');
     space = true; continue;
    }
    sb.Append(c); space = false;
   }
   return sb.ToString().Trim();
  }
  public static string Clip(string text, int max) {
   text = text ?? "";
   return text.Length <= max ? text : text.Substring(0, max) + "…";
  }
  /// <summary>把文本洗成指纹：只保留字母数字，用于查重和「别再提这件事」。</summary>
  public static string Fingerprint(string text) {
   var sb = new StringBuilder();
   text = text ?? "";
   for (int i = 0; i < text.Length; i++) {
    char c = text[i];
    if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
   }
   uint hash = 2166136261;
   string clean = sb.ToString();
   for (int i = 0; i < clean.Length; i++) { hash ^= clean[i]; hash *= 16777619; }
   return hash.ToString("x8");
  }

  static bool IsCjk(char c) { return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF); }
  /// <summary>中文按二元组切分，英文数字按词切分；够小、够稳，不需要词典。</summary>
  public static void Tokens(string text, List<string> list) {
   if (string.IsNullOrEmpty(text)) return;
   int i = 0;
   while (i < text.Length) {
    char c = text[i];
    if (IsCjk(c)) {
     int start = i;
     while (i < text.Length && IsCjk(text[i])) i++;
     int length = i - start;
     if (length == 1) list.Add(text.Substring(start, 1));
     else for (int k = 0; k < length - 1; k++) list.Add(text.Substring(start + k, 2));
    } else if (char.IsLetterOrDigit(c)) {
     int start = i;
     while (i < text.Length && char.IsLetterOrDigit(text[i]) && !IsCjk(text[i])) i++;
     list.Add(text.Substring(start, i - start).ToLowerInvariant());
    } else i++;
   }
  }
  public static double Similarity(string a, string b) {
   var left = new List<string>(); Tokens(a, left);
   var right = new List<string>(); Tokens(b, right);
   if (left.Count == 0 || right.Count == 0) return 0;
   var set = new HashSet<string>(left);
   var other = new HashSet<string>(right);
   int shared = 0;
   foreach (string token in set) if (other.Contains(token)) shared++;
   int union = set.Count + other.Count - shared;
   return union <= 0 ? 0 : (double)shared / union;
  }
 }

}
