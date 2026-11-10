using System;
using System.Text;
using System.Collections.Generic;
using System.Globalization;

namespace ElymsPet {
 /// <summary>从回复里取出记忆标记；标记本身不会显示给对方。</summary>
 public static class MemoryMarkers {
  public static bool HasMarker(string reply) {
   if (string.IsNullOrEmpty(reply)) return false;
   return reply.IndexOf("[[REMEMBER]]", StringComparison.Ordinal) >= 0 || reply.IndexOf("[[MEMORY]]", StringComparison.Ordinal) >= 0;
  }
  public static MemoryItem[] Extract(ref string reply) {
   var items = new List<MemoryItem>();
   string text = reply ?? "";
   if (!HasMarker(text)) return new MemoryItem[0];
   int cursor = 0;
   var kept = new StringBuilder();
   while (cursor < text.Length) {
    int open = NextOpen(text, cursor);
    if (open < 0) { kept.Append(text.Substring(cursor)); break; }
    kept.Append(text.Substring(cursor, open - cursor));
    bool remember = string.Compare(text, open, "[[REMEMBER]]", 0, 12, StringComparison.Ordinal) == 0;
    string openTag = remember ? "[[REMEMBER]]" : "[[MEMORY]]";
    int bodyStart = open + openTag.Length;
    int valueEnd, after;
    if (!TagMarkers.FindClose(text, bodyStart, remember ? "REMEMBER" : "MEMORY", out valueEnd, out after)) { cursor = bodyStart; continue; }
    string body = text.Substring(bodyStart, valueEnd - bodyStart).Trim();
    MemoryItem item = ParseBody(body);
    if (item != null && items.Count < MemoryStore.MaxPerReply) items.Add(item);
    cursor = after;
   }
   reply = Collapse(kept.ToString());
   return items.ToArray();
  }
  static int NextOpen(string text, int from) {
   int a = text.IndexOf("[[REMEMBER]]", from, StringComparison.Ordinal);
   int b = text.IndexOf("[[MEMORY]]", from, StringComparison.Ordinal);
   if (a < 0) return b;
   if (b < 0) return a;
   return a < b ? a : b;
  }
  static string Collapse(string text) {
   text = (text ?? "").Replace("\r\n", "\n").Trim();
   while (text.IndexOf("\n\n\n", StringComparison.Ordinal) >= 0) text = text.Replace("\n\n\n", "\n\n");
   return text;
  }
  static MemoryItem ParseBody(string body) {
   if (string.IsNullOrEmpty(body)) return null;
   body = body.Replace("\r", "\n");
   string[] parts = body.Split(new char[] { ';', '；', '\n', '|', '｜' });
   string text = "", entity = "", emotion = "", kind = "";
   double importance = -1;
   bool core = false;
   DateTime? due = null;
   var loose = new List<string>();
   for (int i = 0; i < parts.Length; i++) {
    string part = parts[i].Trim();
    if (part.Length == 0) continue;
    int eq = part.IndexOf('='); if (eq < 0) eq = part.IndexOf('＝');
    if (eq <= 0) {
     if (MemoryStore.IsKind(part) && kind.Length == 0) kind = part; else loose.Add(part);
     continue;
    }
    string key = part.Substring(0, eq).Trim().ToLowerInvariant();
    string value = part.Substring(eq + 1).Trim();
    if (key == "kind" || key == "type" || key == "类型" || key == "种类") kind = value;
    else if (key == "entity" || key == "主体" || key == "对象") entity = value;
    else if (key == "emotion" || key == "情绪" || key == "心情") emotion = value;
    else if (key == "text" || key == "content" || key == "内容" || key == "记忆") text = value;
    else if (key == "important" || key == "importance" || key == "重要") {
     double parsed;
     if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) importance = parsed;
    } else if (key == "core" || key == "核心") core = value == "true" || value == "1" || value == "是";
    else if (key == "due" || key == "到期" || key == "期限") {
     DateTime parsedDue;
     if (DateTime.TryParse(value, out parsedDue)) due = parsedDue;
    } else loose.Add(part);
   }
   if (text.Length == 0) text = string.Join("，", loose.ToArray());
   text = text.Trim();
   if (text.Length == 0) return null;
   if (!MemoryStore.IsKind(kind)) kind = "事实";
   var item = new MemoryItem { Kind = kind, Text = text, Entity = entity, Emotion = emotion, Due = due, Core = core || kind == "关系" };
   if (importance >= 0) item.Importance = Math.Max(0, Math.Min(1, importance));
   else item.Importance = kind == "承诺" ? 0.8 : (kind == "偏好" || kind == "习惯") ? 0.6 : 0.5;
   item.Source = "明说";
   return item;
  }
 }

}
