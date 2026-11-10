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
  [DataMember] public string Boundary = "已掌握";
  [DataMember] public string Evidence = "";
  [DataMember] public string Note = "";
  [DataMember] public DateTime At = DateTime.MinValue;
  [DataMember] public string Source = "对话整理";
 }
 [DataContract] public sealed class ArchiveTurn {
  [DataMember] public string Role = "ask";
  [DataMember] public string Text = "";
  [DataMember] public DateTime At = DateTime.MinValue;
  [DataMember] public string Sources = "";
  [DataMember] public string Assessment = "";
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
  [DataMember] public string Focus = "";
  [DataMember] public bool Paused;
  [DataMember] public string InterviewStage = "";
  [DataMember] public string InterviewGoal = "";
  [DataMember] public List<KnowledgeLink> Links = new List<KnowledgeLink>();
  [DataMember] public List<ArchiveChange> Changes = new List<ArchiveChange>();
  [DataMember] public List<ArchiveEpisode> Episodes = new List<ArchiveEpisode>();
  [DataMember] public List<ArchiveResource> Resources = new List<ArchiveResource>();
  [DataMember] public List<ArchiveWorkflow> Workflows = new List<ArchiveWorkflow>();
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
  public string Focus = "";
  public List<ArchiveEpisode> Episodes = new List<ArchiveEpisode>();
  public List<KnowledgeLinkProposal> Links = new List<KnowledgeLinkProposal>();
  public bool Pause;
  public List<ArchiveResource> Resources = new List<ArchiveResource>();
  public List<ArchiveWorkflow> Workflows = new List<ArchiveWorkflow>();
 }
 public static class ArchiveStore {
  public const int MaxSlots = 20;
  public const int MaxItems = 300;
  public const int MaxTurns = 1000;
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
   if(book==null)return false;
   return JsonFile.Save(FilePath,Normalize(book),Folder,out LastError);
  }
  public static ArchiveBook Normalize(ArchiveBook book) {
   if(book==null) book=new ArchiveBook();
   book.Version=Math.Max(4,book.Version);
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
    ArchiveKnowledge.Normalize(slot);
    slot.Items=NormalizeItems(slot.Items);
    KnowledgeGraph.Normalize(slot);
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
     string key=Key(item.Domain)+":"+Key(item.Title);if(keys.ContainsKey(key)) continue;
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
     copy.Sources=Clip((turn.Sources??"").Trim(),3000);
     copy.Assessment=copy.Role=="ask"?Clip((turn.Assessment??"").Trim(),2000):"";
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
    string key=Key(item.Domain)+":"+Key(item.Title);
    ArchiveItem existing=null;
    for(int i=0;i<slot.Items.Count;i++) if(Key(slot.Items[i].Domain)+":"+Key(slot.Items[i].Title)==key) {existing=slot.Items[i];break;}
    if(existing!=null) {
     var next=ArchiveLearning.Copy(existing);
     // A repeated vague extraction must not erase a more specific self-report.
     if(item.Evidence.Length>0){next.Level=item.Level;next.Boundary=item.Boundary;next.Evidence=item.Evidence;}
     if(item.Note.Length>0)next.Note=item.Note;
     if(ArchiveLearning.Same(existing,next))continue;
     next.At=DateTime.UtcNow;
     ArchiveLearning.Record(slot,"UPDATE",existing,next);
     slot.Items[slot.Items.IndexOf(existing)]=next;
     continue;
    }
    if(slot.Items.Count>=MaxItems)continue; // Never silently evict established user knowledge.
    slot.Items.Add(item);ArchiveLearning.Record(slot,"ADD",null,item);added++;
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
   text.Append(ArchiveKnowledge.Reuse(slot));
   if(summary.Length>0) text.Append(summary);
   if(ordered.Count>0) {
    if(text.Length>0) text.Append("。知识边界："); else text.Append("知识边界：");
    for(int i=0;i<ordered.Count;i++) {
     text.Append(ordered[i].Domain).Append("·").Append(ordered[i].Title)
      .Append("（").Append(ordered[i].Boundary).Append(" / ").Append(ordered[i].Level).Append("）").Append("；");
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
      text.Append("- [").Append(item.Boundary).Append(" / ").Append(item.Level).Append("] ").Append(item.Title).Append("\r\n");
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
   item.Boundary=ArchiveKnowledge.Boundary(item.Boundary);
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
  public const string SchemaHint = "输出格式：只输出一个 JSON 对象，不要 Markdown 代码块，不要多余文字：{\"reply\":\"依据 assessment 追问关键缺口或提议最小试做\",\"items\":[{\"title\":\"知识点或技能，不超过 20 字\",\"domain\":\"领域，不超过 8 字\",\"level\":\"了解|会用|熟练|专长\",\"evidence\":\"对方原话里的依据，不超过 60 字\",\"note\":\"你的补充，可为空\"}],\"summary\":\"用一句话概括目前画像，没有新增可留空\"}";
  public const string Understanding = "访谈的价值是今后能协助完成整项工作。每轮都思考：如果现在接手这个具体任务，我能基于已有信息可靠地做出什么，还会在哪个决策、操作或验证环节卡住？追问深度由这个实际阻塞决定，不由轮数决定。宏观流程和技术细节都只是候选信息；关键参数、代码路径、判据若会改变方案，就继续深入到可操作、可验证的程度。若已有信息足够，先提出有边界的最小试做并核对结果，不为凑齐表格而继续问。";
  public static string Agenda(ArchiveSlot slot) {
   var text=new StringBuilder("\n协助可行性评估：用已有信息尝试构建完成当前任务的具体方案，检查方案中的假设、缺失输入、决策依据和验证方式；只把简短结论写入 assessment，不展示推理过程。不要把能复述流程骨架误当作能解决实际工作，也不要把缺少某个字段自动当作关键缺口。\n");
   text.Append("先界定用户当前要的交付物与范围，再判断信息是否足够。区分真正阻塞当前交付的必要信息和仅能进一步完善的可选信息：可明确标注待定、列为假设或留给试做验证，且不影响当前交付正确性的内容，不得当作先决问题。用户只要草稿/清单/对已有内容做整理时，不要擅自扩大成真实执行、上线或证明效果；已有信息能完成这个有边界的交付物，就选择 try。\n");
   text.Append("优先选择会改变下一步做法、影响正确性或验收结果的缺口，一次只问一个问题。已给过答案先复用；答案含糊且真正阻塞工作时才换成具体案例、输入输出或判定条件继续深入。没有连续追问轮数上限，不强制按轮次切回宏观。用户明确跳过时尊重其选择；用户不熟悉时记录边界，调整协助方式而非反复考问。\n");
   text.Append("区分信息来源：用户独有的实际约束、经验取舍、内部环境向用户问；公开原理和文档应自行查证，不让用户替你补通用知识。自身工具权限或无法验证的事实要明确标注，不能伪装成用户知识不足。尚未提及不等于不会，外部资料不能证明用户掌握。\n");
   if(slot==null)return text.ToString();
   string goal=string.IsNullOrEmpty(slot.InterviewGoal)?slot.Focus:slot.InterviewGoal;
   text.Append("稳定工作主线：").Append(string.IsNullOrEmpty(goal)?"尚未确定，先明确一项用户希望你协助的具体任务":goal).Append("\n已有背景摘要：").Append(slot.Summary).Append("\n");
   var items=new List<ArchiveItem>(slot.Items);
   string query=(goal??"")+" "+slot.Focus;
   for(int i=Math.Max(0,slot.Turns.Count-4);i<slot.Turns.Count;i++)if(slot.Turns[i].Role=="answer")query+=" "+slot.Turns[i].Text;
   items.Sort(delegate(ArchiveItem x,ArchiveItem y) {return ArchiveRecall.Score(query,y.Title+" "+y.Domain+" "+y.Note).CompareTo(ArchiveRecall.Score(query,x.Title+" "+x.Domain+" "+x.Note));});
   text.Append("相关已存知识及原话依据（历史自述，需结合最新纠正；不是已验证能力）：\n");
   int count=0;foreach(var item in items) {if(count++>=12)break;text.Append(ArchiveStore.Clip(ArchiveLearning.ItemText(item),350)).Append("\n");}
   if(items.Count>12)text.Append("另有未展开条目，当前上下文不是完整知识库；不要据此断言用户没有其他知识。\n");
   text.Append("接下来结合流程草稿、已确认流程、真实案例、知识关系和最近回答重新判断；流程已确认仅代表用户核实过记录，不证明你已经具备工具权限或实际执行成功。\n");
   return text.ToString();
  }
  public static string BuildSystem(ArchiveSlot slot) {
   var text=new StringBuilder();
   text.Append(CharacterPersona.Prompt);
   text.Append("当前任务：存档访谈。用对话了解对方目前掌握的知识，整理成条目存进他的个人画像。用户拒绝追问时不再提问，items 保持空数组。\n");
   text.Append("说话方式：简体中文，自然口语，一到两句话，四十到八十字；不用 Markdown、列表、代码块，不写舞台说明，不堆颜文字。\n");
   text.Append("依据协助可行性评估主动选择下一步，不以访谈必须继续为前提。\n");
   text.Append(Understanding).Append("\n").Append(ArchiveKnowledge.InterviewRules(slot));
   text.Append("访谈规则：\n1. 一次只问一个具体问题，能从对方上一句自然接下去；不要一次抛出多个问题，不要做成问卷。\n2. 先用一句话回应对方刚说的内容，再问下一个问题。\n3. 尽量覆盖这些方面：正在学什么、哪个领域有底气、常用工具或技能、做过什么完整的东西、学新东西的习惯、只是了解还没上手的方向。\n4. 对方说得模糊时，追问具体例子，不要替他补全，也不要夸大。\n5. 不要评价能力高低，不给学习计划，不替对方下人生结论。\n");
   text.Append(SchemaHint).Append(ArchiveAssessment.Schema).Append("\n").Append(ArchiveKnowledge.Schema).Append(ArchiveLearning.Schema).Append(KnowledgeGraph.Schema);
   text.Append("items 只收录对方这句话里明确说到的知识或技能及其知识边界；没有就输出空数组。不要编造对方没说的内容。提问者介绍的论文、源码原理和资料索引都不是用户能力证据；不能把提问中的事实当作用户已掌握。没有明确说独立完成、已经发布、解决了某问题，就不要补上这些成果；遇到问题不等于已经掌握解决方法。\n");
   string digest=ArchiveStore.Digest(slot,20,500);
   text.Append("当前画像：").Append(digest.Length>0?digest:"（还是空的，需要从头问起。）").Append("\n");
   text.Append("本轮必须只输出一个 JSON 对象：不要解释、不要 Markdown、不要代码块，第一个字符是 {，最后一个字符是 }。先形成 assessment，再由 reply 承接并追问关键缺口或提议最小试做；如果用户已经拒绝访谈，就简短接受，不再追问，items 为空数组。\n");
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
   else list[list.Count-1]=new ChatTurn(list[list.Count-1].Role,list[list.Count-1].Content+"\n\n（只输出 JSON 对象，字段：reply、assessment、items、summary、focus、pause、resources、workflows、episodes、links。）");
   return list.ToArray();
  }
  public static ChatTurn[] BuildQuestion(ArchiveSlot slot) {
   var list=new List<ChatTurn>();
   list.Add(new ChatTurn("system",BuildQuestionSystem(slot)));
   if(slot!=null&&slot.Turns!=null) {
    int start=Math.Max(0,slot.Turns.Count-10);
    for(int i=start;i<slot.Turns.Count;i++) list.Add(new ChatTurn(slot.Turns[i].Role=="ask"?"assistant":"user",slot.Turns[i].Text));
   }
   list.Add(new ChatTurn("user",list.Count==1?"（还没有对话）先判断是否明确用户希望你协助的任务；如未明确，再问一个问题。":"先生成 assessment 判断能否协助当前任务，再生成 reply。信息足够时提议最小试做，不要为了继续访谈而编造必要问题。"));
   return list.ToArray();
  }
  public static string BuildQuestionSystem(ArchiveSlot slot) {
   var text=new StringBuilder();
   text.Append(CharacterPersona.Prompt);
   text.Append("当前任务：评估你能否基于已收集的信息协助用户完成具体工作，由评估结果决定是否需要继续访谈。\n");
   text.Append("主动评估当前任务的可协助范围，用自然的对话承接。不要仅因为好奇就提问，也不要以访谈必须继续为前提；必要信息不足才追问，足够时提议最小试做。\n");
   text.Append(Understanding).Append("\n").Append(ArchiveKnowledge.InterviewRules(slot));
   text.Append("若需追问，只问一个具体问题；信息足够则提议试做，不继续搜集可选细节；不要评价能力高低，不要给学习计划，不替对方下人生结论；不探隐私。\n");
   text.Append("说话方式：简体中文，自然口语，一到两句话，四十到八十字；不用 Markdown、列表、代码块，不写舞台说明，不堆颜文字。\n");
   text.Append("只输出 JSON 对象，包含 reply 和 assessment。reply 是面向用户的一两句话（追问一个必要缺口，或提议一个最小试做并请用户核对），不加「问题：」前缀。\n");
   text.Append(ArchiveAssessment.Schema);
   text.Append("这一轮只负责评估如何协助并选择追问或最小试做，不要整理知识条目，也不要输出画像。举例：用户已给出若干项目步骤，只希望整理成供审阅的草稿，未知负责人或操作细节可以标待定，此时选 try；用户希望你诊断一次真实失败，而缺少区分根因的关键证据，此时选 ask。不要为了完善未来的完整执行方案而阻塞眼前的小交付。\n");
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
   system.Append("规则：不要编造；不要把问题当成答案；不要把「想学」写成已经掌握；记录明确自述的掌握和未掌握边界，未谈及不等于不会。\n");
   system.Append("领域用短词，例如：编程、设计、音乐、语言、写作、硬件、管理、游戏、生活技能。\n");
   system.Append("level 只能是：了解、会用、熟练、专长；按对方的自述判断，判断不了就写「了解」。\n");
   system.Append("只输出 JSON：{\"items\":[{\"title\":\"\",\"domain\":\"\",\"level\":\"\",\"evidence\":\"\",\"note\":\"\"}],\"summary\":\"用一至两句概括对方的画像\"}。不要 Markdown 代码块。\n");
   string digest=ArchiveStore.Digest(slot,20,500);
   system.Append(ArchiveKnowledge.InterviewRules(slot)).Append(ArchiveKnowledge.Schema).Append(ArchiveLearning.Schema).Append(KnowledgeGraph.Schema);
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
     ArchiveKnowledge.Parse(map,result);
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
     item.Boundary=MiniJson.GetString(map,"boundary");
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
