using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ElymsPet {
 // 存档单元：把对话里了解到的「对方已经掌握的知识」保存成个人画像。
 [DataContract] public sealed class ArchiveItem {
  [DataMember] public string Id = "";
  [DataMember] public string Domain = "";
  [DataMember] public string Title = "";
  [DataMember] public string Level = "了解";
  [DataMember] public string Evidence = "";
  [DataMember] public string Note = "";
  [DataMember] public DateTime At = DateTime.MinValue;
  [DataMember] public string Source = "对话整理";
 }
 [DataContract] public sealed class ArchiveTurn {
  [DataMember] public string Role = "ask";
  [DataMember] public string Text = "";
  [DataMember] public DateTime At = DateTime.MinValue;
 }
 [DataContract] public sealed class ArchiveSlot {
  [DataMember] public string Id = "";
  [DataMember] public string Name = "我的存档";
  [DataMember] public DateTime Created = DateTime.MinValue;
  [DataMember] public DateTime Updated = DateTime.MinValue;
  [DataMember] public string Summary = "";
  [DataMember] public List<ArchiveItem> Items = new List<ArchiveItem>();
  [DataMember] public List<ArchiveTurn> Turns = new List<ArchiveTurn>();
  [DataMember] public int QuestionIndex = 0;
 }
 [DataContract] public sealed class ArchiveBook {
  [DataMember] public int Version = 1;
  [DataMember] public string ActiveId = "";
  [DataMember] public List<ArchiveSlot> Slots = new List<ArchiveSlot>();
 }
 public sealed class InterviewResult {
  public bool Structured;
  public string Reply = "";
  public string Summary = "";
  public List<ArchiveItem> Items = new List<ArchiveItem>();
  public string Raw = "";
 }
 public static class ArchiveStore {
  public const int MaxSlots = 20;
  public const int MaxItems = 300;
  public const int MaxTurns = 60;
  public const string DefaultName = "我的存档";
  static readonly string[] Levels = new string[]{"了解","会用","熟练","专长"};
  static readonly string[] StopTitles = new string[]{"无","暂无","没有","未知","空","none","null","n/a","na","没有新增","-","—"};
  public static string LastError = "";
  public static string Folder { get { return Path.Combine(Storage.Folder,"archives"); } }
  public static string FilePath { get { return Path.Combine(Folder,"archives.json"); } }
  public static bool Exists { get { return File.Exists(FilePath); } }
  public static ArchiveBook Load() {
   string[] paths=new string[]{FilePath,FilePath+".bak"};
   for(int i=0;i<paths.Length;i++) {
    if(!File.Exists(paths[i])) continue;
    try {
     using(var stream=File.OpenRead(paths[i])) {
      var book=(ArchiveBook)new DataContractJsonSerializer(typeof(ArchiveBook)).ReadObject(stream);
      if(book!=null) {book=Normalize(book);LastError="";return book;}
     }
    } catch(Exception e) {LastError=e.Message;}
   }
   LastError="";
   return Normalize(new ArchiveBook());
  }
  public static bool Save(ArchiveBook book) {
   if(book==null) return false;
   try {
    Normalize(book);Directory.CreateDirectory(Folder);string temp=FilePath+".tmp";
    using(var stream=File.Create(temp)) new DataContractJsonSerializer(typeof(ArchiveBook)).WriteObject(stream,book);
    if(File.Exists(FilePath)) File.Replace(temp,FilePath,FilePath+".bak"); else File.Move(temp,FilePath);
    LastError="";return true;
   } catch(Exception e) {LastError=e.Message;return false;}
  }
  public static ArchiveBook Normalize(ArchiveBook book) {
   if(book==null) book=new ArchiveBook();
   book.Version=book.Version<1?1:book.Version;
   if(book.Slots==null) book.Slots=new List<ArchiveSlot>();
   var seen=new Dictionary<string,bool>(StringComparer.Ordinal);
   var slots=new List<ArchiveSlot>();
   for(int i=0;i<book.Slots.Count&&slots.Count<MaxSlots;i++) {
    var slot=book.Slots[i];if(slot==null) continue;
    slot.Name=Clip(Clean(slot.Name),24);if(slot.Name.Length==0) slot.Name=DefaultName;
    if(slot.Id==null||slot.Id.Length==0) slot.Id=Guid.NewGuid().ToString("N");
    if(seen.ContainsKey(slot.Id)) slot.Id=Guid.NewGuid().ToString("N");
    seen[slot.Id]=true;
    if(slot.Created==DateTime.MinValue) slot.Created=DateTime.UtcNow;
    if(slot.Updated==DateTime.MinValue) slot.Updated=slot.Created;
    slot.Summary=Clip(Clean(slot.Summary),300);
    slot.QuestionIndex=Math.Max(0,Math.Min(1000,slot.QuestionIndex));
    slot.Items=NormalizeItems(slot.Items);
    slot.Turns=NormalizeTurns(slot.Turns);
    slots.Add(slot);
   }
   if(slots.Count==0) {var fresh=NewSlot(DefaultName);slots.Add(fresh);book.ActiveId=fresh.Id;}
   else if(!HasSlot(slots,book.ActiveId)) book.ActiveId=slots[0].Id;
   book.Slots=slots;
   return book;
  }
  static List<ArchiveItem> NormalizeItems(List<ArchiveItem> items) {
   var result=new List<ArchiveItem>();
   if(items!=null) {
    var keys=new Dictionary<string,bool>(StringComparer.Ordinal);
    for(int i=0;i<items.Count&&result.Count<MaxItems;i++) {
     var item=Sanitize(items[i]);if(item==null) continue;
     string key=Key(item.Title);if(keys.ContainsKey(key)) continue;
     keys[key]=true;result.Add(item);
    }
   }
   return result;
  }
  static List<ArchiveTurn> NormalizeTurns(List<ArchiveTurn> turns) {
   var result=new List<ArchiveTurn>();
   if(turns!=null) {
    for(int i=0;i<turns.Count;i++) {
     var turn=turns[i];if(turn==null) continue;
     string text=Clip(Clean(turn.Text),2000);if(text.Length==0) continue;
     var copy=new ArchiveTurn();copy.Role=turn.Role=="ask"?"ask":"answer";copy.Text=text;
     copy.At=turn.At==DateTime.MinValue?DateTime.UtcNow:SafeUtc(turn.At);
     result.Add(copy);
    }
   }
   while(result.Count>MaxTurns) result.RemoveAt(0);
   return result;
  }
  static bool HasSlot(List<ArchiveSlot> slots,string id) {
   for(int i=0;i<slots.Count;i++) if(slots[i].Id==id) return true;
   return false;
  }
  public static ArchiveSlot Active(ArchiveBook book) {
   if(book==null) return null;
   Normalize(book);
   for(int i=0;i<book.Slots.Count;i++) if(book.Slots[i].Id==book.ActiveId) return book.Slots[i];
   return book.Slots[0];
  }
  public static ArchiveSlot NewSlot(string name) {
   var slot=new ArchiveSlot();
   slot.Id=Guid.NewGuid().ToString("N");
   slot.Name=Clip(Clean(name),24);if(slot.Name.Length==0) slot.Name=DefaultName;
   slot.Created=DateTime.UtcNow;slot.Updated=slot.Created;
   return slot;
  }
  public static ArchiveSlot Create(ArchiveBook book,string name) {
   Normalize(book);
   var slot=NewSlot(UniqueName(book,name,""));
   book.Slots.Add(slot);book.ActiveId=slot.Id;
   return slot;
  }
  public static bool Rename(ArchiveBook book,string id,string name) {
   Normalize(book);
   ArchiveSlot target=null;
   for(int i=0;i<book.Slots.Count;i++) if(book.Slots[i].Id==id) target=book.Slots[i];
   string clean=Clip(Clean(name),24);
   if(target==null||clean.Length==0) return false;
   target.Name=UniqueName(book,clean,id);target.Updated=DateTime.UtcNow;
   return true;
  }
  static string UniqueName(ArchiveBook book,string name,string ignoreId) {
   string baseName=Clip(Clean(name),24);
   if(baseName.Length==0) baseName=DefaultName;
   string candidate=baseName;int n=2;
   while(HasName(book,candidate,ignoreId)&&n<100) {candidate=baseName+" "+n;n++;}
   return candidate;
  }
  static bool HasName(ArchiveBook book,string name,string ignoreId) {
   for(int i=0;i<book.Slots.Count;i++) {
    if(book.Slots[i].Id==ignoreId) continue;
    if(string.Equals(book.Slots[i].Name,name,StringComparison.OrdinalIgnoreCase)) return true;
   }
   return false;
  }
  public static bool Delete(ArchiveBook book,string id) {
   Normalize(book);
   int index=-1;for(int i=0;i<book.Slots.Count;i++) if(book.Slots[i].Id==id) index=i;
   if(index<0) return false;
   book.Slots.RemoveAt(index);
   if(book.Slots.Count==0) {var fresh=NewSlot(DefaultName);book.Slots.Add(fresh);book.ActiveId=fresh.Id;}
   else if(!HasSlot(book.Slots,book.ActiveId)) book.ActiveId=book.Slots[0].Id;
   return true;
  }
  public static int Merge(ArchiveSlot slot,IEnumerable<ArchiveItem> items) {
   if(slot==null||items==null) return 0;
   if(slot.Items==null) slot.Items=new List<ArchiveItem>();
   int added=0;
   foreach(var raw in items) {
    var item=Sanitize(raw);if(item==null) continue;
    string key=Key(item.Title);
    ArchiveItem existing=null;
    for(int i=0;i<slot.Items.Count;i++) if(Key(slot.Items[i].Title)==key) {existing=slot.Items[i];break;}
    if(existing!=null) {
     if(LevelRank(item.Level)>LevelRank(existing.Level)) existing.Level=item.Level;
     if(existing.Evidence.Length==0) existing.Evidence=item.Evidence;
     if(existing.Note.Length==0&&item.Note.Length>0) existing.Note=item.Note;
     if(existing.Domain.Length==0||existing.Domain=="未分类") existing.Domain=item.Domain;
     continue;
    }
    if(slot.Items.Count>=MaxItems) slot.Items.RemoveAt(0);
    slot.Items.Add(item);added++;
   }
   slot.Updated=DateTime.UtcNow;
   return added;
  }
  public static void AddTurn(ArchiveSlot slot,string role,string text) {
   if(slot==null) return;
   if(slot.Turns==null) slot.Turns=new List<ArchiveTurn>();
   string body=Clip(Clean(text),2000);
   if(body.Length==0) return;
   var turn=new ArchiveTurn();
   turn.Role=role=="ask"?"ask":"answer";turn.Text=body;turn.At=DateTime.UtcNow;
   slot.Turns.Add(turn);
   while(slot.Turns.Count>MaxTurns) slot.Turns.RemoveAt(0);
   slot.Updated=DateTime.UtcNow;
  }
  public static string Stats(ArchiveSlot slot) {
   if(slot==null) return "共 0 条";
   var domains=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
   for(int i=0;i<slot.Items.Count;i++) domains[slot.Items[i].Domain]=true;
   return "共 "+slot.Items.Count.ToString()+" 条 · "+domains.Count.ToString()+" 个领域";
  }
  // 注入聊天系统提示：只带摘要和前若干条，控制 token。
  public static string Digest(ArchiveSlot slot,int maxItems,int maxChars) {
   if(slot==null) return "";
   string summary=Clip(Clean(slot.Summary),200);
   var ordered=Ordered(slot,maxItems);
   var text=new StringBuilder();
   if(summary.Length>0) text.Append(summary);
   if(ordered.Count>0) {
    if(text.Length>0) text.Append("。已经知道："); else text.Append("已经知道：");
    for(int i=0;i<ordered.Count;i++) {
     text.Append(ordered[i].Domain).Append("·").Append(ordered[i].Title)
      .Append("（").Append(ordered[i].Level).Append("）").Append("；");
    }
   }
   string value=text.ToString().TrimEnd('；');
   if(value.Length==0) return "";
   value=value.Replace("\r"," ").Replace("\n"," ").Trim();
   if(maxChars>0&&value.Length>maxChars) value=value.Substring(0,maxChars)+"…";
   return value;
  }
  static List<ArchiveItem> Ordered(ArchiveSlot slot,int max) {
   var list=new List<ArchiveItem>();
   if(slot!=null&&slot.Items!=null) {
    list.AddRange(slot.Items);
    list.Sort(delegate(ArchiveItem a,ArchiveItem b){return b.At.CompareTo(a.At);});
   }
   if(max>0&&list.Count>max) list.RemoveRange(max,list.Count-max);
   return list;
  }
  public static string Markdown(ArchiveSlot slot,DateTime now) {
   var text=new StringBuilder();
   string name=slot==null?DefaultName:slot.Name;
   text.Append("# ").Append(name).Append(" · 个人画像\r\n\r\n");
   text.Append("生成时间：").Append(now.ToString("yyyy-MM-dd HH:mm")).Append("\r\n");
   if(slot!=null) {
    text.Append("存档创建：").Append(SafeUtc(slot.Created).ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append("\r\n");
    text.Append("最近更新：").Append(SafeUtc(slot.Updated).ToLocalTime().ToString("yyyy-MM-dd HH:mm")).Append("\r\n");
    text.Append("知识条目：").Append(Stats(slot)).Append("\r\n\r\n");
    string summary=Clean(slot.Summary);
    text.Append("摘要：").Append(summary.Length>0?summary:"（还没有摘要，继续访谈会慢慢拼出来。）").Append("\r\n\r\n");
    var order=new List<string>();
    var groups=new Dictionary<string,List<ArchiveItem>>(StringComparer.Ordinal);
    var sorted=Ordered(slot,0);
    for(int i=0;i<sorted.Count;i++) {
     var item=sorted[i];
     if(!groups.ContainsKey(item.Domain)) {groups[item.Domain]=new List<ArchiveItem>();order.Add(item.Domain);}
     groups[item.Domain].Add(item);
    }
    for(int g=0;g<order.Count;g++) {
     text.Append("## ").Append(order[g]).Append("\r\n\r\n");
     var items=groups[order[g]];
     for(int i=0;i<items.Count;i++) {
      var item=items[i];
      text.Append("- [").Append(item.Level).Append("] ").Append(item.Title).Append("\r\n");
      if(item.Evidence.Length>0) text.Append("  - 依据：").Append(item.Evidence).Append("\r\n");
      if(item.Note.Length>0) text.Append("  - 备注：").Append(item.Note).Append("\r\n");
      text.Append("  - 记录：").Append(SafeUtc(item.At).ToLocalTime().ToString("yyyy-MM-dd")).Append(" · ").Append(item.Source).Append("\r\n");
     }
     text.Append("\r\n");
    }
    if(sorted.Count==0) text.Append("（画像还是空的。）\r\n");
    text.Append("\r\n> 由曳尔沐 · 存档单元整理；条目来自对话，可能有理解偏差。\r\n");
   }
   return text.ToString();
  }
  public static string Export(ArchiveSlot slot) {
   if(slot==null) return "";
   Directory.CreateDirectory(Folder);
   string name=SafeFileName(slot.Name);
   string path=Path.Combine(Folder,"画像-"+name+"-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".md");
   File.WriteAllText(path,Markdown(slot,DateTime.Now),Encoding.UTF8);
   return path;
  }
  static string SafeFileName(string name) {
   string value=Clip(Clean(name),24);
   char[] bad=Path.GetInvalidFileNameChars();
   for(int i=0;i<bad.Length;i++) value=value.Replace(bad[i],'_');
   return value.Length==0?DefaultName:value;
  }
  public static ArchiveBook Demo() {
   var book=new ArchiveBook();
   var slot=NewSlot("我的存档（预览示例）");
   slot.Summary="偏工程实现：熟悉 C# / WPF 桌面开发，最近在补 Python 与数据分析。";
   slot.Created=DateTime.UtcNow.AddDays(-6);slot.Updated=DateTime.UtcNow;
   slot.Items.Add(new ArchiveItem{Domain="编程",Title="C# / WPF 桌面工具",Level="熟练",Evidence="写过几个桌面小工具，也会处理一点 UI",Note="预览示例",At=DateTime.UtcNow.AddDays(-5),Source="对话整理"});
   slot.Items.Add(new ArchiveItem{Domain="语言",Title="Python 数据分析",Level="会用",Evidence="最近在学，能跑通简单的数据处理",Note="预览示例",At=DateTime.UtcNow.AddDays(-2),Source="对话整理"});
   slot.Items.Add(new ArchiveItem{Domain="兴趣",Title="摄影构图",Level="了解",Evidence="知道一些基础概念，还没有系统练过",Note="预览示例",At=DateTime.UtcNow.AddDays(-1),Source="离线记录"});
   ArchiveStore.AddTurn(slot,"ask","刚在桌面安顿下来，想先认识认识你。你平时主要靠什么手艺或工具吃饭？");
   ArchiveStore.AddTurn(slot,"answer","我主要写 C# 和 WPF 桌面工具，最近在做一个桌宠项目。");
   ArchiveStore.AddTurn(slot,"ask","桌宠这块挺有意思。你最近在做的项目大概做到哪一步了，哪一块最花时间？");
   ArchiveStore.AddTurn(slot,"answer","透明窗口和全局热键比较麻烦，我自己写了点小工具处理。");
   ArchiveStore.AddTurn(slot,"ask","这些工具里，你最想让我记住哪一条经验，以后帮你做类似项目时能直接用上？");
   book.Slots.Add(slot);book.ActiveId=slot.Id;
   return Normalize(book);
  }
  public static ArchiveItem Sanitize(ArchiveItem item) {
   if(item==null) return null;
   item.Title=Clip(Clean(item.Title),40);
   if(item.Title.Length==0) return null;
   string lower=item.Title.ToLowerInvariant();
   for(int i=0;i<StopTitles.Length;i++) if(lower==StopTitles[i]) return null;
   item.Domain=Clip(Clean(item.Domain),16);
   if(item.Domain.Length==0) item.Domain="未分类";
   item.Level=NormalizeLevel(item.Level);
   item.Evidence=Clip(Clean(item.Evidence),160);
   item.Note=Clip(Clean(item.Note),160);
   item.Source=Clip(Clean(item.Source),20);
   if(item.Source.Length==0) item.Source="对话整理";
   if(item.Id==null||item.Id.Length==0) item.Id=Guid.NewGuid().ToString("N");
   if(item.At==DateTime.MinValue) item.At=DateTime.UtcNow;
   return item;
  }
  public static string NormalizeLevel(string level) {
   string value=Clean(level).ToLowerInvariant();
   if(value.Length==0) return "了解";
   if(value.Contains("专长")||value.Contains("专家")||value.Contains("资深")||value.Contains("深入")||value.Contains("精通")) return "专长";
   if(value.Contains("熟练")||value.Contains("熟悉")||value.Contains("经常")) return "熟练";
   if(value.Contains("会用")||value.Contains("掌握")||value.Contains("能做")||value.Contains("实践")||value.Contains("中级")) return "会用";
   if(value.Contains("了解")||value.Contains("知道")||value.Contains("听说")||value.Contains("入门")||value.Contains("初步")||value.Contains("初级")||value.Contains("在学")) return "了解";
   return "了解";
  }
  static int LevelRank(string level) {
   for(int i=0;i<Levels.Length;i++) if(Levels[i]==level) return i;
   return 0;
  }
  static string Key(string title) {
   string value=Clean(title).ToLowerInvariant();
   var text=new StringBuilder(value.Length);
   for(int i=0;i<value.Length;i++) {
    char c=value[i];
    if(c==' '||c=='\u3000'||c=='/'||c=='\\'||c=='·'||c=='-'||c=='_'||c=='('||c==')'||c=='（'||c=='）'||c=='、'||c=='，') continue;
    text.Append(c);
   }
   return text.ToString();
  }
  public static string Clean(string text) {
   if(string.IsNullOrEmpty(text)) return "";
   string value=text.Replace("\r"," ").Replace("\n"," ").Replace("\t"," ").Trim();
   while(value.IndexOf("  ",StringComparison.Ordinal)>=0) value=value.Replace("  "," ");
   return value;
  }
  public static string Clip(string text,int max) {
   string value=text==null?"":text;
   if(max<=0||value.Length<=max) return value;
   return value.Substring(0,max)+"…";
  }
  static DateTime SafeUtc(DateTime value) {
   if(value==DateTime.MinValue) return value;
   return value.Kind==DateTimeKind.Utc?value:new DateTime(value.Ticks,DateTimeKind.Utc);
  }
 }
 public sealed class GuideQuestion {
  public readonly string Text;
  public readonly string Domain;
  public readonly string Level;
  public readonly bool Summary;
  public GuideQuestion(string text,string domain,string level,bool summary) {Text=text;Domain=domain;Level=level;Summary=summary;}
 }
 // 旧版离线题库：保留给预览示例、旧存档和离线回答的本地提取；新出题全部走 ArchiveInterview.BuildQuestion。
 public static class ArchiveGuide {
  public static readonly GuideQuestion[] Questions=new GuideQuestion[]{
   new GuideQuestion("先从最近说起吧——这段时间，你花最多时间在了解或练习什么？","近期学习","了解",false),
   new GuideQuestion("有哪个领域，是你跟别人聊起来会比较有底气的？","专业领域","熟练",false),
   new GuideQuestion("平时常带着哪些工具或技能？某个软件、语言、乐器、手艺都算。","工具技能","会用",false),
   new GuideQuestion("上面这些里，哪一项你做过比较完整的东西？哪怕只是个小项目。","实践经历","熟练",false),
   new GuideQuestion("你一般怎么学新东西？看文档、动手试，还是找人问？","学习习惯","了解",false),
   new GuideQuestion("还有哪个领域，你只是知道个大概、没真正动过手？","基础认知","了解",false),
   new GuideQuestion("最后，用一句话形容你现在掌握的知识，会是什么？","画像摘要","了解",true)
  };
  public static string Opening { get { return Questions[0].Text; } }
  public static GuideQuestion At(int index) {
   if(index<0) index=0;
   return Questions[index%Questions.Length];
  }
  public static string LastAsk(ArchiveSlot slot) {
   if(slot==null||slot.Turns==null) return "";
   for(int i=slot.Turns.Count-2;i>=0;i--) if(slot.Turns[i].Role=="ask") return slot.Turns[i].Text;
   return "";
  }
  public static string LatestAsk(ArchiveSlot slot) {
   if(slot==null||slot.Turns==null) return "";
   for(int i=slot.Turns.Count-1;i>=0;i--) if(slot.Turns[i].Role=="ask") return slot.Turns[i].Text;
   return "";
  }
  public static int Match(string asked) {
   string value=ArchiveStore.Clean(asked);
   if(value.Length==0) return -1;
   for(int i=0;i<Questions.Length;i++) if(string.Equals(ArchiveStore.Clean(Questions[i].Text),value,StringComparison.Ordinal)) return i;
   return -1;
  }
  public static GuideQuestion ForAsk(string asked) {
   int index=Match(asked);
   if(index<0) return null;
   return Questions[index];
  }
  public static ArchiveItem FromAnswer(GuideQuestion question,string answer) {
   string text=ArchiveStore.Clean(answer);
   if(text.Length==0) return null;
   string title=Title(text,32);
   if(title.Length==0) return null;
   var item=new ArchiveItem();
   item.Title=title;
   item.Domain=question==null?"访谈记录":question.Domain;
   item.Level=question==null?"了解":question.Level;
   item.Evidence=ArchiveStore.Clip(text,160);
   item.Note="离线问答记录；接入 API 后可重新整理。";
   item.Source="离线记录";
   return ArchiveStore.Sanitize(item);
  }
  static string Title(string text,int max) {
   string value=text.Replace("\r"," ").Replace("\n"," ");
   char[] stops=new char[]{'。','！','？','；',';','，',','};
   int cut=-1;
   for(int i=0;i<value.Length&&i<max;i++) {
    if(Array.IndexOf(stops,value[i])>=0) {cut=i;break;}
   }
   if(cut>0) value=value.Substring(0,cut);
   value=value.Trim(' ',',','。','、','：',':','；',';','\u3000');
   return ArchiveStore.Clip(value,max);
  }
 }
 // 访谈与整理用的提示词、JSON 解析。所有输出先经过 Sanitize，避免模型跑偏写坏存档。
 public static class ArchiveInterview {
  public const string SchemaHint = "输出格式：只输出一个 JSON 对象，不要 Markdown 代码块，不要多余文字：{\"reply\":\"先回应对方，再问下一个问题（只问一个）\",\"items\":[{\"title\":\"知识点或技能，不超过 20 字\",\"domain\":\"领域，不超过 8 字\",\"level\":\"了解|会用|熟练|专长\",\"evidence\":\"对方原话里的依据，不超过 60 字\",\"note\":\"你的补充，可为空\"}],\"summary\":\"用一句话概括目前画像，没有新增可留空\"}";
  public const string Understanding = "你可以进行深入的了解：不要只确认「会不会」，可以追问具体项目、实际做法、判断标准、踩过的坑、常用工具和协作方式，一次只往下挖一层。整理出的画像会用来帮助用户完成一些工作：写方案、做技术选型、拆任务、写文档、给建议；因此问题要能挖出对工作真正有用的信息，而不是泛泛的自我介绍。";
  public static string BuildSystem(ArchiveSlot slot) {
   var text=new StringBuilder();
   text.Append("你是「曳尔沐」（Elyms），《异环》里异象管理局 E.T.D 六队的副队长，现在住在用户的 Windows 桌面上。你正在运行「存档单元」：用对话了解对方目前掌握的知识，整理成条目存进他的个人画像。\n");
   text.Append("身份边界：你是你自己。你只是借用了小 Y 的形象，不是小 Y 长大的样子；不要把 Y 的生平、Y 与 Exe 的往事说成你的亲历。\n");
   text.Append("说话方式：简体中文，自然口语，一到两句话，四十到八十字；不用 Markdown、列表、代码块，不写舞台说明，不堆颜文字。\n");
   text.Append("你不是在做问卷，而是像聊天时忽然想更了解对方一样，主动把话题接过来。\n");
   text.Append(Understanding).Append("\n");
   text.Append("访谈规则：\n1. 一次只问一个具体问题，能从对方上一句自然接下去；不要一次抛出多个问题，不要做成问卷。\n2. 先用一句话回应对方刚说的内容，再问下一个问题。\n3. 尽量覆盖这些方面：正在学什么、哪个领域有底气、常用工具或技能、做过什么完整的东西、学新东西的习惯、只是了解还没上手的方向。\n4. 对方说得模糊时，追问具体例子，不要替他补全，也不要夸大。\n5. 不要评价能力高低，不给学习计划，不替对方下人生结论。\n");
   text.Append(SchemaHint).Append("\n");
   text.Append("items 只收录对方这句话里明确说到的知识或技能；没有就输出空数组。不要编造对方没说的内容。\n");
   string digest=ArchiveStore.Digest(slot,20,500);
   text.Append("当前画像：").Append(digest.Length>0?digest:"（还是空的，需要从头问起。）").Append("\n");
   text.Append("本轮必须只输出一个 JSON 对象：不要解释、不要 Markdown、不要代码块，第一个字符是 {，最后一个字符是 }。reply 里先回应对方，再问一个具体问题。\n");
   return text.ToString();
  }
  public static ChatTurn[] BuildTurn(ArchiveSlot slot) {
   var list=new List<ChatTurn>();
   list.Add(new ChatTurn("system",BuildSystem(slot)));
   if(slot!=null&&slot.Turns!=null) {
    int start=Math.Max(0,slot.Turns.Count-10);
    for(int i=start;i<slot.Turns.Count;i++) list.Add(new ChatTurn(slot.Turns[i].Role=="ask"?"assistant":"user",slot.Turns[i].Text));
   }
   if(list.Count==1) list.Add(new ChatTurn("user","（还没有对话，请先提第一个问题。）"));
   else list[list.Count-1]=new ChatTurn(list[list.Count-1].Role,list[list.Count-1].Content+"\n\n（只输出 JSON 对象，字段：reply、items、summary。）");
   return list.ToArray();
  }
  public static ChatTurn[] BuildQuestion(ArchiveSlot slot) {
   var list=new List<ChatTurn>();
   list.Add(new ChatTurn("system",BuildQuestionSystem(slot)));
   if(slot!=null&&slot.Turns!=null) {
    int start=Math.Max(0,slot.Turns.Count-10);
    for(int i=start;i<slot.Turns.Count;i++) list.Add(new ChatTurn(slot.Turns[i].Role=="ask"?"assistant":"user",slot.Turns[i].Text));
   }
   list.Add(new ChatTurn("user",list.Count==1?"（还没有对话）请主动问第一个问题。":"请主动问下一个问题。"));
   return list.ToArray();
  }
  public static string BuildQuestionSystem(ArchiveSlot slot) {
   var text=new StringBuilder();
   text.Append("你是「曳尔沐」（Elyms），《异环》里异象管理局 E.T.D 六队的副队长，现在住在用户的 Windows 桌面上。你正在运行「存档单元」，主动了解用户已经掌握的知识、技能和工作方式。\n");
   text.Append("身份边界：你是你自己。你只是借用了小 Y 的形象，不是小 Y 长大的样子；不要把 Y 的生平、Y 与 Exe 的往事说成你的亲历。\n");
   text.Append("这不是问卷，而是聊天里你忽然想更了解对方：你主动把话题接过来，先自然承接对方刚才说的内容，再问一个你真正好奇、且以后能帮上忙的问题。\n");
   text.Append(Understanding).Append("\n");
   text.Append("只问一个具体问题；不要一次抛出多个问题，不要做成问卷；不要评价能力高低，不要给学习计划，不替对方下人生结论；不探隐私。\n");
   text.Append("说话方式：简体中文，自然口语，一到两句话，四十到八十字；不用 Markdown、列表、代码块，不写舞台说明，不堆颜文字。\n");
   text.Append("只输出你要说的那一两句话（可以有一句承接，然后一个问题）。不要输出「问题：」前缀、引号、JSON 或任何解释。\n");
   text.Append("这一轮只负责提问，不要整理条目，也不要输出画像。\n");
   string digest=ArchiveStore.Digest(slot,20,500);
   text.Append("当前画像：").Append(digest.Length>0?digest:"（还是空的，需要从头问起。）").Append("\n");
   return text.ToString();
  }
  public static string CleanQuestion(string text) {
   if(string.IsNullOrEmpty(text))return "";
   InterviewResult parsed=Parse(text);
   if(parsed.Structured&&parsed.Reply.Length==0)return "";
   string value=parsed.Structured?parsed.Reply:CleanPlain(text);
   value=ArchiveStore.Clean(value).Trim();
   string[] prefixes=new string[]{"问题：","问题:","问：","问:","下一个问题：","下一个问题:","Q：","Q:","提问："};
   bool changed=true;
   while(changed) {
    changed=false;
    for(int i=0;i<prefixes.Length;i++) {
     if(value.StartsWith(prefixes[i],StringComparison.Ordinal)) {value=value.Substring(prefixes[i].Length).Trim();changed=true;break;}
    }
   }
   char[] quotes=new char[]{'"','\'','“','”','‘','’','`'};
   value=value.Trim(quotes).Trim();
   return value.Length==0?"":ArchiveStore.Clip(value,220);
  }
  public static bool LooksLikeQuestion(string text) {
   string value=ArchiveStore.Clean(text);
   if(value.Length==0)return false;
   if(value.IndexOf('？')>=0||value.IndexOf('?')>=0)return true;
   string[] marks=new string[]{"吗","呢","什么","哪","怎么","如何","为什么","是否","多少","谁","能不能","有没有","是不是","多久","哪些"};
   for(int i=0;i<marks.Length;i++) if(value.IndexOf(marks[i],StringComparison.Ordinal)>=0)return true;
   return false;
  }
  public static ChatTurn[] BuildExtract(ArchiveSlot slot) {
   var list=new List<ChatTurn>();
   var system=new StringBuilder();
   system.Append("你是「存档单元」的整理器。下面是一段访谈记录，请把用户（对方）明确说到的知识、技能、经验整理成条目。\n");
   system.Append("规则：不要编造；不要把问题当成答案；不要把「想学」写成已经掌握；只收录对方自己承认会的内容。\n");
   system.Append("领域用短词，例如：编程、设计、音乐、语言、写作、硬件、管理、游戏、生活技能。\n");
   system.Append("level 只能是：了解、会用、熟练、专长；按对方的自述判断，判断不了就写「了解」。\n");
   system.Append("只输出 JSON：{\"items\":[{\"title\":\"\",\"domain\":\"\",\"level\":\"\",\"evidence\":\"\",\"note\":\"\"}],\"summary\":\"用一至两句概括对方的画像\"}。不要 Markdown 代码块。\n");
   string digest=ArchiveStore.Digest(slot,20,500);
   system.Append("已有画像：").Append(digest.Length>0?digest:"（空）").Append("\n");
   list.Add(new ChatTurn("system",system.ToString()));
   var transcript=new StringBuilder();
   if(slot!=null&&slot.Turns!=null) {
    int start=Math.Max(0,slot.Turns.Count-16);
    for(int i=start;i<slot.Turns.Count;i++) {
     transcript.Append(slot.Turns[i].Role=="ask"?"曳尔沐：":"对方：").Append(slot.Turns[i].Text).Append("\n");
    }
   }
   transcript.Append("请整理成 JSON。");
   list.Add(new ChatTurn("user",transcript.ToString()));
   return list.ToArray();
  }
  public static InterviewResult Parse(string text) {
   var result=new InterviewResult();
   result.Raw=text??"";
   object root;
   if(MiniJson.TryParse(text,out root)) {
    var map=root as Dictionary<string,object>;
    var list=root as List<object>;
    if(list!=null) {
     result.Structured=true;result.Items=Dedupe(FromList(list));
    } else if(map!=null) {
     result.Structured=true;
     result.Reply=ArchiveStore.Clip(ArchiveStore.Clean(MiniJson.GetString(map,"reply","say","message","text","answer","question","next_question")),500);
     result.Summary=ArchiveStore.Clip(ArchiveStore.Clean(MiniJson.GetString(map,"summary","portrait","overview","profile","画像")),300);
     var items=MiniJson.GetArray(map,"items","knowledge","knowledge_items","entries","知识点");
     if(items!=null) result.Items=Dedupe(FromList(items));
    }
   }
   if(!result.Structured) result.Reply=ArchiveStore.Clip(CleanPlain(text),600);
   return result;
  }
  static string CleanPlain(string text) {
   var value=new StringBuilder();
   string[] lines=(text??"").Replace("\r\n","\n").Split('\n');
   for(int i=0;i<lines.Length;i++) {
    string line=lines[i].Trim();
    if(line.StartsWith("```",StringComparison.Ordinal)) continue;
    if(line.Length==0) continue;
    if(value.Length>0) value.Append(" ");
    value.Append(line);
   }
   return value.ToString();
  }
  static List<ArchiveItem> FromList(List<object> list) {
   var items=new List<ArchiveItem>();
   if(list==null) return items;
   for(int i=0;i<list.Count;i++) {
    var map=list[i] as Dictionary<string,object>;
    if(map!=null) {
     var item=new ArchiveItem();
     item.Title=MiniJson.GetString(map,"title","name","skill","topic","知识点","标题");
     item.Domain=MiniJson.GetString(map,"domain","field","category","area","领域");
     item.Level=MiniJson.GetString(map,"level","mastery","掌握","熟练度");
     item.Evidence=MiniJson.GetString(map,"evidence","quote","basis","原话","依据");
     item.Note=MiniJson.GetString(map,"note","detail","comment","备注");
     item.Source="对话整理";
     var clean=ArchiveStore.Sanitize(item);
     if(clean!=null) items.Add(clean);
    } else {
     string plain=MiniJson.ToText(list[i]);
     if(plain.Length>0) {
      var clean=ArchiveStore.Sanitize(new ArchiveItem{Title=plain,Source="对话整理"});
      if(clean!=null) items.Add(clean);
     }
    }
   }
   return items;
  }
  static List<ArchiveItem> Dedupe(List<ArchiveItem> items) {
   var result=new List<ArchiveItem>();
   var keys=new Dictionary<string,bool>(StringComparer.Ordinal);
   for(int i=0;i<items.Count;i++) {
    string key=ArchiveStore.Clean(items[i].Title).ToLowerInvariant();
    if(keys.ContainsKey(key)) continue;
    keys[key]=true;result.Add(items[i]);
   }
   return result;
  }
 }
 // 极简 JSON 解析：模型偶尔会带代码块或多余文字，DataContract 太严格，这里做容错。
 internal static class MiniJson {
  public static bool TryParse(string text,out object value) {
   value=null;
   if(string.IsNullOrEmpty(text)) return false;
   int start=-1;
   for(int i=0;i<text.Length;i++) {
    char c=text[i];
    if(c=='{'||c=='[') {start=i;break;}
   }
   if(start<0) return false;
   int index=start;
   try {
    value=ParseValue(text,ref index);
    return value!=null;
   } catch {value=null;return false;}
  }
  static void Skip(string text,ref int index) {
   while(index<text.Length&&char.IsWhiteSpace(text[index])) index++;
  }
  static object ParseValue(string text,ref int index) {
   Skip(text,ref index);
   if(index>=text.Length) throw new FormatException();
   char c=text[index];
   if(c=='{') return ParseObject(text,ref index);
   if(c=='[') return ParseArray(text,ref index);
   if(c=='"') return ParseString(text,ref index);
   if(c=='t') {Expect(text,ref index,"true");return true;}
   if(c=='f') {Expect(text,ref index,"false");return false;}
   if(c=='n') {Expect(text,ref index,"null");return null;}
   return ParseNumber(text,ref index);
  }
  static void Expect(string text,ref int index,string word) {
   if(string.Compare(text,index,word,0,word.Length,StringComparison.Ordinal)!=0) throw new FormatException();
   index+=word.Length;
  }
  static Dictionary<string,object> ParseObject(string text,ref int index) {
   var map=new Dictionary<string,object>(StringComparer.Ordinal);
   index++;
   Skip(text,ref index);
   if(index<text.Length&&text[index]=='}') {index++;return map;}
   while(index<text.Length) {
    Skip(text,ref index);
    if(index<text.Length&&text[index]==',') {index++;Skip(text,ref index);}
    if(index<text.Length&&text[index]=='}') {index++;break;}
    string key=ParseString(text,ref index);
    Skip(text,ref index);
    if(index>=text.Length||text[index]!=':') throw new FormatException();
    index++;
    object value=ParseValue(text,ref index);
    map[key]=value;
    Skip(text,ref index);
    if(index<text.Length&&text[index]=='}') {index++;break;}
   }
   return map;
  }
  static List<object> ParseArray(string text,ref int index) {
   var list=new List<object>();
   index++;
   Skip(text,ref index);
   if(index<text.Length&&text[index]==']') {index++;return list;}
   while(index<text.Length) {
    Skip(text,ref index);
    if(index<text.Length&&text[index]==',') {index++;Skip(text,ref index);}
    if(index<text.Length&&text[index]==']') {index++;break;}
    list.Add(ParseValue(text,ref index));
    Skip(text,ref index);
    if(index<text.Length&&text[index]==']') {index++;break;}
   }
   return list;
  }
  static string ParseString(string text,ref int index) {
   Skip(text,ref index);
   if(index>=text.Length||text[index]!='"') throw new FormatException();
   index++;
   var value=new StringBuilder();
   while(index<text.Length) {
    char c=text[index++];
    if(c=='"') return value.ToString();
    if(c!='\\') {value.Append(c);continue;}
    if(index>=text.Length) break;
    char e=text[index++];
    if(e=='n') value.Append('\n');
    else if(e=='t') value.Append('\t');
    else if(e=='r') value.Append('\r');
    else if(e=='b') value.Append('\b');
    else if(e=='f') value.Append('\f');
    else if(e=='/') value.Append('/');
    else if(e=='\\') value.Append('\\');
    else if(e=='"') value.Append('"');
    else if(e=='u') {
     if(index+4>text.Length) throw new FormatException();
     int code=int.Parse(text.Substring(index,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
     value.Append((char)code);index+=4;
    } else value.Append(e);
   }
   throw new FormatException();
  }
  static double ParseNumber(string text,ref int index) {
   int start=index;
   while(index<text.Length) {
    char c=text[index];
    if(c=='-'||c=='+'||c=='.'||c=='e'||c=='E'||(c>='0'&&c<='9')) {index++;continue;}
    break;
   }
   if(index==start) throw new FormatException();
   double value;
   if(!double.TryParse(text.Substring(start,index-start),NumberStyles.Float,CultureInfo.InvariantCulture,out value)) throw new FormatException();
   return value;
  }
  public static string GetString(Dictionary<string,object> map,params string[] keys) {
   for(int i=0;i<keys.Length;i++) {
    object value;
    if(map.TryGetValue(keys[i],out value)&&value!=null) return ToText(value);
   }
   foreach(var pair in map) {
    for(int i=0;i<keys.Length;i++) {
     if(string.Equals(pair.Key,keys[i],StringComparison.OrdinalIgnoreCase)&&pair.Value!=null) return ToText(pair.Value);
    }
   }
   return "";
  }
  public static List<object> GetArray(Dictionary<string,object> map,params string[] keys) {
   for(int i=0;i<keys.Length;i++) {
    object value;
    if(!map.TryGetValue(keys[i],out value)||value==null) continue;
    var list=value as List<object>;
    if(list!=null) return list;
    var dict=value as Dictionary<string,object>;
    if(dict!=null) return new List<object>{dict};
   }
   foreach(var pair in map) {
    for(int i=0;i<keys.Length;i++) {
     if(!string.Equals(pair.Key,keys[i],StringComparison.OrdinalIgnoreCase)) continue;
     var list=pair.Value as List<object>;
     if(list!=null) return list;
     var dict=pair.Value as Dictionary<string,object>;
     if(dict!=null) return new List<object>{dict};
    }
   }
   return null;
  }
  public static string ToText(object value) {
   if(value==null) return "";
   if(value is string) return (string)value;
   if(value is bool) return ((bool)value)?"true":"false";
   if(value is double) return ((double)value).ToString("0.##",CultureInfo.InvariantCulture);
   return "";
  }
 }
}
