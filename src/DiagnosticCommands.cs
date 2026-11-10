using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ElymsPet {
 internal static class DiagnosticCommands {
  internal static int RunOfflineTests() {
   string runner=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ElymsPet.Tests.exe");
   if(!File.Exists(runner)) {Console.Error.WriteLine("请先运行 build.ps1 -Tests 或 test.ps1，生成独立离线测试程序。");return 2;}
   using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(runner) {UseShellExecute=false,CreateNoWindow=true})) {
    process.WaitForExit();return process.ExitCode;
   }
  }
  /// <summary>记忆功能的端到端验收：真实对话 → 抽取记忆 → 检索召回；全部在临时目录里跑，不碰真实记忆。</summary>
  internal static int MemoryBench() {
   var real=Storage.Load();
   var resolved=ApiKey.Resolve(real);
   string key=resolved.Key,baseUrl=real.ApiBase,model=real.ApiModel;
   var report=new StringBuilder();
   try {Directory.CreateDirectory("tests");} catch {}
   if(!ApiKey.Valid(key)) {
    File.WriteAllText(Path.Combine("tests","memory-bench.txt"),"FAIL\r\n没有可用的 API Key，无法做真实对话验收。\r\n",Encoding.UTF8);
    return 1;
   }
   string temp=Path.Combine(Path.GetTempPath(),"ElymsMemoryBench-"+Guid.NewGuid().ToString("N"));
   string oldFolder=Storage.Folder;
   bool pass=true;
   try {
    Storage.Folder=temp;
    var state=Storage.Load();
    var book=MemoryStore.NewBook();
    var session=new ChatSession();
    string[] script={
     "我叫林舟，做后端开发，主要写 C# 和 Go。",
     "我喝咖啡只喝冰美式，不加糖。",
     "下周三是我妈的生日。",
     "我不太喜欢吵闹的环境，人多的地方会累。",
     "我家有只橘猫，叫团子。",
     "再说一次，我喝咖啡只喝冰美式，不加糖。",
     "下午想喝点东西，推荐一下？",
     "我妈生日快到了，送什么好？",
     "团子最近老抓沙发，怎么办？",
     "帮我写个快速排序。"
    };
    report.Append("memory-bench · 真实对话验收\r\n");
    report.Append("model=").Append(model).Append("  上限=").Append(MemoryStore.MaxItems).Append(" 条\r\n");
    for(int i=0;i<script.Length;i++) {
     string user=script[i];
     string context=MemoryStore.Recall(book,user,true);
     int recalled=CountMemoryLines(context);
     ChatTurn[] turns=session.Build(new ConversationRequest(state,user,"","","",context,"",DateTime.Now));
     ChatOutcome outcome=DeepSeek.Complete(key,baseUrl,model,true,turns,90);
     report.Append("\r\n[").Append(i+1).Append("] 用户：").Append(user).Append("\r\n");
     report.Append("    召回 ").Append(recalled).Append(" 条");
     if(recalled>0) report.Append("：").Append(Flatten(context));
     report.Append("\r\n");
     if(!outcome.Ok) {report.Append("    失败：").Append(outcome.Error).Append("\r\n");pass=false;break;}
     string reply=outcome.Text;
     MemoryItem[] items=MemoryMarkers.Extract(ref reply);
     int added=MemoryStore.Add(book,items);
     session.Remember(user,ChatText.Clip(reply,800));
     report.Append("    写入 ").Append(items.Length).Append(" 条");
     if(items.Length>0) {
      report.Append("（新增 ").Append(added).Append("）");
      for(int k=0;k<items.Length;k++) report.Append("\r\n      · ").Append(items[k].Kind).Append("｜").Append(items[k].Text);
     }
     report.Append("\r\n    她：").Append(Flatten(ChatText.Clip(reply,120))).Append("\r\n");
    }
    // 检索质量探针
    int coffeeCount=0;
    for(int i=0;i<book.Items.Length;i++) {
     MemoryItem item=book.Items[i];
     if(item==null)continue;
     if(item.Text.IndexOf("咖啡",StringComparison.Ordinal)>=0||(item.Entity??"").IndexOf("咖啡",StringComparison.Ordinal)>=0)coffeeCount++;
    }
    string recallCoffee=MemoryStore.Recall(book,"想喝杯咖啡，推荐一下？",true);
    string recallVague=MemoryStore.Recall(book,"下午想喝点东西，推荐一下？",true);
    string recallBirthday=MemoryStore.Recall(book,"我妈生日快到了，送什么好？",true);
    string recallCat=MemoryStore.Recall(book,"团子最近老抓沙发，怎么办？",true);
    string recallNone=MemoryStore.Recall(book,"帮我写个快速排序。",true);
    int birthdayLines=CountMemoryLines(recallBirthday);
    bool dedupe=coffeeCount==1;
    bool hitCoffee=recallCoffee.IndexOf("冰美式",StringComparison.Ordinal)>=0;
    bool hitBirthday=recallBirthday.IndexOf("生日",StringComparison.Ordinal)>=0||recallBirthday.IndexOf("周三",StringComparison.Ordinal)>=0;
    bool hitCat=recallCat.IndexOf("团子",StringComparison.Ordinal)>=0||recallCat.IndexOf("橘猫",StringComparison.Ordinal)>=0;
    bool hitNone=recallNone.IndexOf("还没有",StringComparison.Ordinal)>=0;
    bool narrow=birthdayLines<=3;
    pass=pass&&dedupe&&hitCoffee&&hitBirthday&&hitCat&&hitNone&&narrow;
    report.Append("\r\n=== 检索探针 ===\r\n");
    report.Append("咖啡条目数=").Append(coffeeCount).Append(dedupe?"（去重正确）":"（应为 1）").Append("\r\n");
    report.Append("问「想喝杯咖啡」  → ").Append(Mark(hitCoffee)).Append("  ").Append(Flatten(recallCoffee)).Append("\r\n");
    report.Append("问「我妈生日」    → ").Append(Mark(hitBirthday)).Append("（").Append(birthdayLines).Append(" 条）").Append("  ").Append(Flatten(recallBirthday)).Append("\r\n");
    report.Append("问「团子抓沙发」  → ").Append(Mark(hitCat)).Append("  ").Append(Flatten(recallCat)).Append("\r\n");
    report.Append("问「快速排序」    → ").Append(Mark(hitNone)).Append("  ").Append(Flatten(recallNone)).Append("\r\n");
    report.Append("问「喝点东西」（口语，无共同词）→ [观察]  ").Append(Flatten(recallVague)).Append("\r\n");
    report.Append("噪声控制：无关条目 ").Append(narrow?"未混入":"混入了").Append("（生日问题只召回 ").Append(birthdayLines).Append(" 条）\r\n");
    report.Append("\r\n=== 记忆库（").Append(book.Items.Length).Append(" 条） ===\r\n");
    MemoryItem[] all=MemoryStore.Sorted(book);
    for(int i=0;i<all.Length;i++) {
     if(all[i]==null)continue;
     report.Append("· ").Append(all[i].Kind).Append("｜").Append(all[i].Text)
      .Append("  [次数 ").Append(all[i].Hits).Append(all[i].Core?" · 核心":"").Append("]\r\n");
    }
    report.Append("\r\n").Append(pass?"PASS":"FAIL").Append("\r\n");
   } catch(Exception e) {
    report.Append("FAIL\r\n").Append(e).Append("\r\n");
    pass=false;
   } finally {
    Storage.Folder=oldFolder;
    try {if(Directory.Exists(temp))Directory.Delete(temp,true);} catch {}
   }
   try {File.WriteAllText(Path.Combine("tests","memory-bench.txt"),report.ToString(),Encoding.UTF8);} catch {}
   return pass?0:1;
  }
  static int CountMemoryLines(string text) {
   if(string.IsNullOrEmpty(text))return 0;
   int count=0;int index=0;
   while(index<text.Length) {
    int end=text.IndexOf('\n',index);
    if(end<0)end=text.Length;
    if(text.Substring(index,end-index).StartsWith("- ",StringComparison.Ordinal))count++;
    index=end+1;
   }
   return count;
  }
  static string Flatten(string text) {
   if(string.IsNullOrEmpty(text))return "";
   string value=text.Replace("\r"," ").Replace("\n"," / ").Trim();
   while(value.IndexOf("  ",StringComparison.Ordinal)>=0)value=value.Replace("  "," ");
   return value.Length>160?value.Substring(0,160)+"…":value;
  }
  static string Mark(bool ok) {return ok?"[OK]":"[MISS]";}
  /// <summary>导出人设与几种状态下的完整系统提示，便于逐字检查，不联网。</summary>
  internal static int PersonaDump() {
   var report=new StringBuilder();
   DateTime now=new DateTime(2026,11,9,21,30,0);
   string dsh="DSH 工作台在线，工作区：D:\\code\\12m。";
   string web="联网可用：DeepSeek Search / Fetch 在线。";
   string memory="记忆可用。你记得的事：\n- 11-07 偏好｜只喝冰美式，不加糖\n- 11-05 事件｜下周三（11月18日）是妈妈的生日\n- 11-01 关系｜约定过忙到很晚也要留一句晚安";
   string profile="C# / WPF 桌面工具（熟练）；Python 数据分析（了解）";
   report.Append("CharacterPersona.Version=").Append(CharacterPersona.Version).Append("\r\n");
   report.Append("人物设定字符数=").Append(CharacterPersona.Prompt.Length)
    .Append("  估算 tokens=").Append(ChatSession.EstimateTokens(CharacterPersona.Prompt)).Append("\r\n\r\n");
   report.Append("========== 人设原文（assets/persona/elyms.txt） ==========\r\n");
   report.Append(CharacterPersona.Prompt).Append("\r\n");
   string[] names={"基础（无 DSH / 联网 / 记忆）","DSH 在线","DSH + 联网","记忆可用（3 条）","专注中","睡眠 + 画像","记忆关闭"};
   string[] prompts={
    Persona.SystemPrompt(SampleState(),now),
    Persona.SystemPrompt(SampleState(),now,"",dsh,""),
    Persona.SystemPrompt(SampleState(),now,"",dsh,web),
    Persona.SystemPrompt(SampleState(),now,"",dsh,web,memory),
    Focused(now,dsh,web),
    Sleeping(now,profile),
    Persona.SystemPrompt(SampleState(),now,"",dsh,web,"记忆关闭了。")
   };
   for(int i=0;i<prompts.Length;i++) {
    report.Append("\r\n========== ").Append(names[i]).Append(" ==========\r\n");
    report.Append("字符数=").Append(prompts[i].Length).Append("  估算 tokens=").Append(ChatSession.EstimateTokens(prompts[i])).Append("\r\n");
    report.Append(prompts[i]).Append("\r\n");
   }
   report.Append("\r\n========== 存档访谈（BuildSystem，前 1400 字） ==========\r\n");
   string interview=ArchiveInterview.BuildSystem(ArchiveStore.NewSlot("示例"));
   report.Append(interview.Length>1400?interview.Substring(0,1400)+"…":interview).Append("\r\n");
   report.Append("\r\n========== 主动出题（BuildQuestionSystem，前 900 字） ==========\r\n");
   string question=ArchiveInterview.BuildQuestionSystem(ArchiveStore.NewSlot("示例"));
   report.Append(question.Length>900?question.Substring(0,900)+"…":question).Append("\r\n");
   report.Append("\r\n========== 离线对白 ==========\r\n");
   string[] inputs={"你不就是长大的 Y 吗？","我忙，先别说话。","今天好累啊。","你的生日是哪天？","帮我看下我的代码"};
   for(int i=0;i<inputs.Length;i++) report.Append("用户：").Append(inputs[i]).Append("\r\n曳尔沐：").Append(Dialogue.Reply(inputs[i])).Append("\r\n");
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests","persona.txt"),report.ToString(),Encoding.UTF8);} catch {}
   return 0;
  }
  static PetState SampleState() { return new PetState {Friendship=42,Energy=73}; }
  static string Focused(DateTime now,string dsh,string web) {
   PetState state=SampleState();
   state.StartFocus(25,"专注",now.AddMinutes(-10).ToUniversalTime());
   return Persona.SystemPrompt(state,now,"",dsh,web);
  }
  static string Sleeping(DateTime now,string profile) {
   PetState state=SampleState();
   state.Sleeping=true;
   return Persona.SystemPrompt(state,now,profile);
  }
  internal static int UiSmoke() {
   string temp=Path.Combine(Path.GetTempPath(),"ElymsUiSmoke-"+Guid.NewGuid().ToString("N"));
   string oldFolder=Storage.Folder;
   var report=new StringBuilder();
   bool pass=false;
   try {
    Storage.Folder=temp;
    System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
    var pet=new PetWindow(new PetState(),true);
    var panel=new ControlWindow(pet);
    var bar=new ChatBarWindow(pet);
    var panelRoot=(FrameworkElement)panel.Content;
    var barRoot=(FrameworkElement)bar.Content;
    Size panelSize=Layout(panelRoot,850,800);
    Size barSize=Layout(barRoot,486,720);
    for(int i=0;i<panel.Tabs.Items.Count;i++) {
     panel.Tabs.SelectedIndex=i;
     panelRoot.UpdateLayout();
    }
    panel.Tabs.SelectedIndex=0;
    Layout(panelRoot,850,800);
    report.Append("tabs=").Append(panel.Tabs.Items.Count).Append("\r\n");
    report.Append("panel=").Append(panelSize.Width).Append("x").Append(panelSize.Height).Append("\r\n");
    report.Append("bar=").Append(barSize.Width).Append("x").Append(barSize.Height).Append("\r\n");
    pass=panelSize.Width>0&&panelSize.Height>0&&barSize.Width>0&&barSize.Height>0;
    report.Insert(0,pass?"PASS\r\n":"FAIL: 窗口内容没有完成布局\r\n");
    try {bar.Close();} catch {}
    try {panel.Close();} catch {}
    try {pet.Close();} catch {}
   } catch(Exception e) {
    report.Append("FAIL: ").Append(e).Append("\r\n");
   } finally {
    Storage.Folder=oldFolder;
    try {if(Directory.Exists(temp))Directory.Delete(temp,true);} catch {}
   }
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests","ui-smoke.txt"),report.ToString(),Encoding.UTF8);} catch {}
   return pass?0:1;
  }
  static Size Layout(FrameworkElement content,double width,double height) {
   content.Measure(new Size(width,height));
   content.Arrange(new Rect(0,0,width,height));
   content.UpdateLayout();
   return new Size(content.ActualWidth,content.ActualHeight);
  }
  internal static int DshCheck() {
   var text=new StringBuilder();
   bool pass=false;
   try {
    var health=DshApi.Health();
    text.Append(health.Ok?"PASS":"FAIL").Append("\r\n");
    text.Append("health=").Append(health.Ok?"ok":health.Error).Append("\r\n");
    text.Append("workspace=").Append(health.Workspace).Append("\r\n");
    if(health.Ok) {
     DshJob job=DshApi.Start("这是一次连通性测试。请只回复两个字：接通。不要使用任何工具，不要修改文件，不要打开窗口。","read-only","桌宠 DSH 连通性测试");
     text.Append("job=").Append(job.Id).Append("\r\n");
     var start=DateTime.UtcNow;
     while(job.Busy&&(DateTime.UtcNow-start).TotalSeconds<180) {Thread.Sleep(2000);job=DshApi.Poll(job.Id);}
     text.Append("status=").Append(job.Status).Append("\r\n");
     text.Append("session=").Append(job.SessionId).Append("\r\n");
     text.Append("tools=").Append(job.Tools).Append("\r\n");
     text.Append("seconds=").Append(job.Seconds.ToString("0.0")).Append("\r\n");
     text.Append("reply=").Append(job.Text).Append("\r\n");
     text.Append("error=").Append(job.Error).Append("\r\n");
     pass=job.Status=="done"&&job.Text.Trim().Length>0;
    }
   } catch(Exception e) {text.Append("FAIL: ").Append(e).Append("\r\n");}
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests","dsh-test.txt"),text.ToString(),Encoding.UTF8);} catch {}
   return pass?0:1;
  }
  internal static int ApiCheck(bool chat,string prompt) {
   var state=Storage.Load();
   var resolvedKey=ApiKey.Resolve(state);
   string origin=resolvedKey.Origin;string key=resolvedKey.Key;
   string digest="";
   if(chat) {
    var book=ArchiveStore.Load();
    digest=ArchiveStore.Digest(ArchiveStore.Active(book),10,340);
   }
   ChatSession cliSession=new ChatSession();
   string memoryContext=chat?MemoryStore.Recall(MemoryStore.Load(),prompt,state.MemoryEnabled):"";
   ChatTurn[] turns=chat?cliSession.Build(new ConversationRequest(state,prompt,digest,DshApi.PromptContext(state),WebApi.PromptContext(state),memoryContext,"",DateTime.Now)):Persona.Connectivity();
   bool thinking=chat;
   ChatOutcome outcome=DeepSeek.Complete(key,state.ApiBase,state.ApiModel,thinking,turns,60);
   Directory.CreateDirectory("tests");
   string path=Path.Combine("tests",chat?"chat-test.txt":"api-test.txt");
   var text=new StringBuilder();
   text.Append(outcome.Ok?"PASS":"FAIL").Append("\r\n");
   text.Append("model=").Append(state.ApiModel).Append("\r\n");
   text.Append("base=").Append(DeepSeek.NormalizeBaseUrl(state.ApiBase)).Append("\r\n");
   text.Append("key=").Append(ApiKey.Mask(key)).Append(" (来源：").Append(origin).Append(")\r\n");
   text.Append("seconds=").Append(outcome.Seconds.ToString("0.00")).Append("\r\n");
   text.Append("status=").Append(outcome.Status).Append("\r\n");
   text.Append("tokens=").Append(outcome.PromptTokens).Append("+").Append(outcome.CompletionTokens).Append("\r\n");
   text.Append("thinking=").Append(thinking?"on":"off").Append("\r\n");
   if(chat)text.Append("prompt=").Append(prompt).Append("\r\n");
   if(chat)text.Append("memory=").Append(memoryContext).Append("\r\n");
   text.Append("reply=").Append(outcome.Text).Append("\r\n");
   if(chat)text.Append("marker=").Append(MemoryMarkers.HasMarker(outcome.Text)?"yes":"no").Append("\r\n");
   text.Append("error=").Append(outcome.Error).Append("\r\n");
   text.Append("detail=").Append(outcome.Detail).Append("\r\n");
   File.WriteAllText(path,text.ToString(),Encoding.UTF8);
   return outcome.Ok?0:1;
  }
  internal static int WebCheck(bool fetch,string value) {
   var state=Storage.Load();
   var resolved=ApiKey.Resolve(state);
   string key=resolved.Key,origin=resolved.Origin;
   WebOutcome outcome=fetch?WebApi.Fetch(key,state.ApiBase,value,45):WebApi.Search(key,state.ApiBase,value,5,true,45);
   var text=new StringBuilder();
   text.Append(outcome.Ok?"PASS":"FAIL").Append("\r\n");
   text.Append("kind=").Append(fetch?"fetch":"search").Append("\r\n");
   text.Append("base=").Append(DeepSeek.NormalizeBaseUrl(state.ApiBase)).Append("\r\n");
   text.Append("key=").Append(ApiKey.Mask(key)).Append(" (来源：").Append(origin).Append(")\r\n");
   text.Append("seconds=").Append(outcome.Seconds.ToString("0.00")).Append("\r\n");
   text.Append("status=").Append(outcome.Status).Append("\r\n");
   text.Append("sources=").Append(outcome.SourceCount).Append("\r\n");
   text.Append("error=").Append(outcome.Error).Append("\r\n");
   text.Append("detail=").Append(outcome.Detail).Append("\r\n");
   if(fetch) {
    text.Append("title=").Append(outcome.Title).Append("\r\n");
    text.Append("url=").Append(outcome.Url).Append("\r\n");
    text.Append("content=").Append(DshTask.Clip(outcome.Content,2000)).Append("\r\n");
   } else {
    text.Append("query=").Append(value).Append("\r\n");
    if(outcome.Results!=null) {
     for(int i=0;i<outcome.Results.Length;i++) {
      WebSource source=outcome.Results[i];
      text.Append("result=").Append(i+1).Append(" | ").Append(source.title).Append(" | ").Append(source.url).Append(" | ").Append(source.site_name).Append(" | ").Append(DshTask.Clip(source.BestText,240)).Append("\r\n");
     }
    }
   }
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests",fetch?"web-fetch-test.txt":"web-test.txt"),text.ToString(),Encoding.UTF8);} catch {}
   return outcome.Ok&&outcome.SourceCount>0?0:1;
  }
  internal static int WebChatCheck(string query) {
   var state=Storage.Load();
   var resolved=ApiKey.Resolve(state);
   string key=resolved.Key,origin=resolved.Origin;
   WebOutcome search=WebApi.Search(key,state.ApiBase,query,5,true,45);
   ChatOutcome answer=null;
   string prompt="";
   if(search.Ok&&search.SourceCount>0) {
    string evidence=WebApi.ModelEvidence(query,search);
    prompt=WebApi.BuildAnswerPrompt(query,evidence);
    ChatSession session=new ChatSession();
    ChatTurn[] turns=session.Build(new ConversationRequest(state,query,"",DshApi.PromptContext(state),WebApi.PromptContext(state),"",evidence,DateTime.Now));
    answer=DeepSeek.Complete(key,state.ApiBase,state.ApiModel,true,turns,60,1600,0.4);
   }
   var text=new StringBuilder();
   bool pass=search.Ok&&search.SourceCount>0&&answer!=null&&answer.Ok&&answer.Text.Trim().Length>0;
   text.Append(pass?"PASS":"FAIL").Append("\r\n");
   text.Append("query=").Append(query).Append("\r\n");
   text.Append("base=").Append(DeepSeek.NormalizeBaseUrl(state.ApiBase)).Append("\r\n");
   text.Append("key=").Append(ApiKey.Mask(key)).Append(" (来源：").Append(origin).Append(")\r\n");
   text.Append("search_seconds=").Append(search.Seconds.ToString("0.00")).Append("\r\n");
   text.Append("sources=").Append(search.SourceCount).Append("\r\n");
   text.Append("search_error=").Append(search.Error).Append("\r\n");
   if(answer!=null) {
    text.Append("answer_seconds=").Append(answer.Seconds.ToString("0.00")).Append("\r\n");
    text.Append("tokens=").Append(answer.PromptTokens).Append("+").Append(answer.CompletionTokens).Append("\r\n");
    text.Append("thinking=on\r\n");
    text.Append("answer=").Append(answer.Text).Append("\r\n");
    text.Append("answer_error=").Append(answer.Error).Append("\r\n");
   }
   if(search.Results!=null) for(int i=0;i<search.Results.Length;i++) text.Append("source=").Append(i+1).Append(" | ").Append(search.Results[i].title).Append(" | ").Append(search.Results[i].url).Append("\r\n");
   text.Append("prompt=").Append(DshTask.Clip(prompt,1500)).Append("\r\n");
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests","web-chat-test.txt"),text.ToString(),Encoding.UTF8);} catch {}
   return pass?0:1;
  }
  internal static int BiliCheck(string query) {
   var text=new StringBuilder();
   bool pass=false;
   try {
    BiliVideo[] list=BiliApi.Search(query,5,30);
    text.Append(list.Length>0?"PASS":"FAIL").Append("\r\n");
    text.Append("query=").Append(query).Append("\r\n");
    text.Append("videos=").Append(list.Length).Append("\r\n");
    for(int i=0;i<list.Length;i++) text.Append("video=").Append(i+1).Append(" | ").Append(list[i].Bvid).Append(" | ").Append(list[i].Title).Append(" | ").Append(list[i].Author).Append(" | ").Append(list[i].PlayText).Append("\r\n");
    if(list.Length>0) {
     BiliVideo video=BiliApi.LoadVideo(list[0].Bvid,30);
     text.Append("selected=").Append(video.Bvid).Append(" | ").Append(video.Title).Append(" | aid=").Append(video.Aid).Append(" | cid=").Append(video.Cid).Append("\r\n");
     BiliComment[] comments=BiliApi.Comments(video,5,30);
     text.Append("comments=").Append(comments.Length).Append("\r\n");
     for(int i=0;i<comments.Length;i++) text.Append("comment=").Append(i+1).Append(" | ").Append(comments[i].Author).Append(" | ").Append(DshTask.Clip(comments[i].Message,200)).Append(" | ").Append(comments[i].Like).Append("\r\n");
     pass=video.Aid>0;
    }
   } catch(Exception e) {text.Append("FAIL\r\nerror=").Append(e).Append("\r\n");}
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests","bili-test.txt"),text.ToString(),Encoding.UTF8);} catch {}
   return pass?0:1;
  }
  internal static int BiliChatCheck(string query) {
   var state=Storage.Load();
   var resolved=ApiKey.Resolve(state);
   var book=ArchiveStore.Load();
   string digest=ArchiveStore.Digest(ArchiveStore.Active(book),10,340);
   BiliBrowseResult result=BiliExplorer.Explore(query,resolved.Key,state.AiChat&&resolved.Configured,state.ApiBase,state.ApiModel,state,digest,DshApi.PromptContext(state),WebApi.PromptContext(state));
   var text=new StringBuilder();
   bool pass=result!=null&&result.Ok&&!string.IsNullOrEmpty(result.Summary);
   text.Append(pass?"PASS":"FAIL").Append("\r\n");
   text.Append("topic=").Append(result==null?"":result.Topic).Append("\r\n");
   text.Append("seconds=").Append(result==null?"":result.Seconds.ToString("0.00")).Append("\r\n");
   text.Append("videos=").Append(result==null||result.Videos==null?0:result.Videos.Length).Append("\r\n");
   text.Append("selected=").Append(result==null||result.Selected==null?"":result.Selected.Bvid+" | "+result.Selected.Title).Append("\r\n");
   text.Append("comments=").Append(result==null||result.Comments==null?0:result.Comments.Length).Append("\r\n");
   text.Append("summary=").Append(result==null?"":result.Summary).Append("\r\n");
   text.Append("error=").Append(result==null?"":result.Error).Append("\r\n");
   text.Append("note=").Append(result==null?"":result.Note).Append("\r\n");
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests","bili-chat-test.txt"),text.ToString(),Encoding.UTF8);} catch {}
   return pass?0:1;
  }
  internal static int BiliDeleteCheck(string bvid,string rpidText) {
   var text=new StringBuilder();
   bool pass=false;
   try {
    BiliAuth.Load();
    long rpid;long.TryParse(rpidText,out rpid);
    text.Append("bvid=").Append(bvid).Append("\r\n");
    text.Append("rpid=").Append(rpid).Append("\r\n");
    text.Append("logged_in=").Append(BiliApi.HasLogin).Append("\r\n");
    if(!BiliApi.HasLogin) {text.Append("FAIL\r\nerror=没有登录\r\n");}
    else if(rpid<=0) {text.Append("FAIL\r\nerror=rpid 无效\r\n");}
    else {
     BiliVideo video=BiliApi.LoadVideo(bvid,30);
     text.Append("aid=").Append(video.Aid).Append("\r\n");
     BiliCommentResult result=BiliApi.DeleteComment(video,rpid,BiliApi.CsrfToken(),30);
     text.Append(result.Ok?"PASS":"FAIL").Append("\r\n");
     text.Append("error=").Append(result.Error).Append("\r\n");
     pass=result.Ok;
    }
   } catch(Exception e) {text.Append("FAIL\r\nerror=").Append(e).Append("\r\n");}
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests","bili-delete-test.txt"),text.ToString(),Encoding.UTF8);} catch {}
   return pass?0:1;
  }
  internal static int ArchiveInit() {
   var text=new StringBuilder();
   bool saved=false;
   try {
    var book=ArchiveStore.Load();
    var slot=ArchiveStore.Active(book);
    saved=ArchiveStore.Save(book);
    text.Append(saved?"PASS":"FAIL").Append("\r\n");
    text.Append("file=").Append(ArchiveStore.FilePath).Append("\r\n");
    text.Append("exists=").Append(ArchiveStore.Exists).Append("\r\n");
    text.Append("archive=").Append(slot.Name).Append("\r\n");
    text.Append("items=").Append(slot.Items.Count).Append("\r\n");
   } catch(Exception e) {text.Append("FAIL\r\nerror=").Append(e.Message).Append("\r\n");}
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests","archive-init.txt"),text.ToString(),Encoding.UTF8);} catch {}
   return saved?0:1;
  }
  internal static int ArchiveCheck(string answer) {
   var real=Storage.Load();
   var realKey=ApiKey.Resolve(real);string origin=realKey.Origin;string key=realKey.Key;
   if(answer==null||answer.Trim().Length==0) answer="我平时主要写 C# 和 WPF，做过几个桌面小工具，最近在学 Python 和数据分析。";
   string temp=Path.Combine(Path.GetTempPath(),"ElymsArchiveTest-"+Guid.NewGuid().ToString("N"));
   string oldFolder=Storage.Folder;
   var report=new StringBuilder();
   bool pass=false,structured=false,saved=false;
   int added=0,persisted=0,status=0,tokens=0;
   double seconds=0;
   string reply="",summary="",error="";
   try {
    Storage.Folder=temp;
    var book=ArchiveStore.Load();
    var slot=ArchiveStore.Active(book);
    ArchiveStore.AddTurn(slot,"ask","刚在桌面安顿下来，想先认识认识你。你平时主要靠什么手艺或工具吃饭？");
    ArchiveStore.AddTurn(slot,"answer",answer);
    ChatOutcome outcome=DeepSeek.Complete(key,DeepSeek.NormalizeBaseUrl(real.ApiBase),real.ApiModel,false,ArchiveInterview.BuildTurn(slot),60,1400,0.4);
    seconds=outcome.Seconds;tokens=outcome.CompletionTokens;status=outcome.Status;error=outcome.Error;reply=outcome.Text;
    InterviewResult parsed=ArchiveInterview.Parse(outcome.Text);
    bool turnStructured=parsed.Structured;
    structured=parsed.Structured;summary=parsed.Summary;
    if(parsed.Reply.Length>0) ArchiveStore.AddTurn(slot,"ask",parsed.Reply);
    added=ArchiveStore.Merge(slot,parsed.Items);
    if(parsed.Summary.Length>0) slot.Summary=parsed.Summary;
    string extractInfo="";
    if(!turnStructured) {
     ChatOutcome extract=DeepSeek.Complete(key,DeepSeek.NormalizeBaseUrl(real.ApiBase),real.ApiModel,false,ArchiveInterview.BuildExtract(slot),60,1800,0.2);
     InterviewResult extracted=ArchiveInterview.Parse(extract.Text);
     structured=extracted.Structured;
     int extractAdded=ArchiveStore.Merge(slot,extracted.Items);
     added+=extractAdded;
     if(extracted.Summary.Length>0) summary=extracted.Summary;
     if(!extract.Ok) error=extract.Error;
     extractInfo="turn_structured="+turnStructured+"\r\nextract_structured="+extracted.Structured+"\r\nextract_added="+extractAdded+"\r\nextract_seconds="+extract.Seconds.ToString("0.00")+"\r\nextract_tokens="+extract.CompletionTokens+"\r\n";
    }
    saved=ArchiveStore.Save(book);
    var reloaded=ArchiveStore.Load();
    persisted=reloaded.Slots.Count>0?reloaded.Slots[0].Items.Count:0;
    pass=outcome.Ok&&structured&&saved&&persisted==added&&added>0;
    report.Append(pass?"PASS":"FAIL").Append("\r\n");
    report.Append("model=").Append(real.ApiModel).Append("\r\n");
    report.Append("base=").Append(DeepSeek.NormalizeBaseUrl(real.ApiBase)).Append("\r\n");
    report.Append("key=").Append(ApiKey.Mask(key)).Append(" (来源：").Append(origin).Append(")\r\n");
    report.Append("seconds=").Append(outcome.Seconds.ToString("0.00")).Append("\r\n");
    report.Append("status=").Append(status).Append("\r\n");
    report.Append("tokens=").Append(outcome.PromptTokens).Append("+").Append(tokens).Append("\r\n");
    report.Append("answer=").Append(answer).Append("\r\n");
    report.Append("structured=").Append(structured).Append("\r\n").Append(extractInfo);
    report.Append("reply=").Append(reply).Append("\r\n");
    report.Append("summary=").Append(summary).Append("\r\n");
    report.Append("added=").Append(added).Append("\r\n");
    for(int i=0;i<slot.Items.Count;i++) {
     ArchiveItem item=slot.Items[i];
     report.Append("item=").Append(item.Domain).Append(" | ").Append(item.Title).Append(" | ").Append(item.Level)
      .Append(" | ").Append(item.Evidence).Append("\r\n");
    }
    report.Append("persisted=").Append(persisted).Append("\r\n");
    report.Append("file=").Append(ArchiveStore.FilePath).Append("\r\n");
    report.Append("error=").Append(error).Append("\r\n");
   } catch(Exception e) {
    report.Append("FAIL\r\nerror=").Append(e.Message).Append("\r\n");
   } finally {Storage.Folder=oldFolder;try {if(Directory.Exists(temp))Directory.Delete(temp,true);} catch {}}
   try {Directory.CreateDirectory("tests");File.WriteAllText(Path.Combine("tests","archive-test.txt"),report.ToString(),Encoding.UTF8);} catch {}
   return pass?0:1;
  }
  internal static void Render(FrameworkElement element,string path) {
   var window=element as Window;if(window!=null&&window.Content is FrameworkElement)element=(FrameworkElement)window.Content;
   var bitmap=new RenderTargetBitmap((int)element.ActualWidth,(int)element.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var s=File.Create(path))encoder.Save(s);
  }
 }
}
