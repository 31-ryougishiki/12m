using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ElymsPet {
 static class ShareAttachmentTests {
  static int checks;
  const string Share="https://chat.deepseek.com/share/testshare12345678";
  static void Check(bool value,string name){if(!value)throw new Exception("Share attachment: "+name);checks++;Console.WriteLine("PASS share attachment: "+name);}
  static string FileJson(string id,string name,int size,string extra="") {
   return "{\"id\":\""+id+"\",\"file_name\":\""+name+"\",\"file_size\":"+size+",\"status\":\"SUCCESS\",\"signed_path\":\"/file?file_id="+id+"&state=synthetic-signature\""+extra+"}";
  }
  static string ShareJson(params string[] files){return "{\"code\":0,\"data\":{\"biz_code\":0,\"biz_data\":{\"title\":\"fixture\",\"messages\":[{\"role\":\"USER\",\"fragments\":[{\"type\":\"FILE\",\"files\":["+string.Join(",",files)+"]}]},{\"role\":\"ASSISTANT\",\"fragments\":[{\"type\":\"THINK\",\"content\":\"PUBLIC_THINK\"},{\"type\":\"RESPONSE\",\"content\":\"PUBLIC_ANSWER\"}]}]}}}";}
  public static void Run(string root) {
   var first=Encoding.UTF8.GetBytes("# 中文脚本\nkv_producer\nFIRST_END");var second=Encoding.UTF8.GetBytes("kv_consumer\nSECOND_END");
   var download=new Download(ShareJson(FileJson("file-one","prefill.txt",first.Length),FileJson("file-two","decode.txt",second.Length)),first,second);
   var outcome=DeepSeekShare.Fetch(Share,30,download);
   Check(outcome.Ok&&outcome.Attachments.Count==2&&outcome.Attachments.All(a=>a.Status=="已完整读取"),"reads two signed public attachments");
   Check(download.Urls.Count==3&&download.Urls[0].Contains("/api/v0/share/content?")&&download.Urls[1].StartsWith("https://files.deepseeksvc.com/api/file?"),"follows public metadata to official file service only");
   Check(outcome.Content.Contains("FIRST_END")&&outcome.Content.Contains("SECOND_END")&&outcome.Content.Contains("PUBLIC_ANSWER"),"page and both attachment bodies survive composition");
   Check(!outcome.Content.Contains("synthetic-signature")&&!outcome.Content.Contains("signed_path")&&outcome.Attachments[0].SourceUrl==Share+"#attachment-file-one","signed capability is not persisted or sent to model");
   Check(outcome.Content.Contains("不是最终结论"),"public thinking fragment is clearly labeled");
   string evidence=WebApi.ModelEvidence(Share,outcome);
   Check(evidence.Contains("FIRST_END")&&evidence.Contains("SECOND_END")&&evidence.Contains("只是参考资料"),"model receives both scripts as untrusted data");
   Check(WebApi.BuildAnswerPrompt("比较附件",evidence).Contains("SECOND_END"),"final chat prompt retains second attachment");
   string guide="GUIDE_START\n"+new string('流',3400)+"\nGUIDE_END";byte[] wiki=Encoding.UTF8.GetBytes("WIKI_START\n"+new string('网',8900)+"\nWIKI_END");
   string mixed=ShareJson(FileJson("wiki","network.txt",wiki.Length)).Replace("\"role\":\"USER\",\"fragments\":[","\"role\":\"USER\",\"fragments\":[{\"type\":\"REQUEST\",\"content\":"+GraphEditing.Json(guide)+"},");
   var full=DeepSeekShare.Fetch(Share,30,new Download(mixed,wiki));string fullEvidence=WebApi.ModelEvidence(Share,full);
   Check(full.PageText.Contains(guide)&&full.Attachments[0].Text.EndsWith("WIKI_END"),"REQUEST guide and attached wiki are both read completely");
   Check(fullEvidence.Contains(guide)&&fullEvidence.Contains(Encoding.UTF8.GetString(wiki)),"model evidence preserves full 3K guide and 9K attachment without a second cutoff");
   Check(WebApi.BuildAnswerPrompt("整理测试流程",fullEvidence).Contains("GUIDE_END")&&WebApi.BuildAnswerPrompt("整理测试流程",fullEvidence).Contains("WIKI_END"),"final answer prompt retains both document endings");
   var fullApi=new ResearchApi(full);var fullSlot=ArchiveStore.NewSlot("full-guide");var fullResearch=ArchiveResearch.Ask(fullApi,new PetState{WebEnabled=true},"test",ArchiveInterview.BuildQuestion(fullSlot),"整理测试流程",new List<string>{Share});
   Check(fullApi.LastInput.Contains(guide)&&fullApi.LastInput.Contains("WIKI_END"),"archive research receives the guide and full attachment too");
   var textOnly=DeepSeekShare.Fetch(Share,30,new Download(ShareJson().Replace("\"role\":\"USER\",\"fragments\":[","\"role\":\"USER\",\"fragments\":[{\"type\":\"REQUEST\",\"content\":"+GraphEditing.Json(guide)+"},")));
   Check(textOnly.Content.Contains("GUIDE_END"),"text-only share no longer loses content after the old 3000-character cap");
   var answerApi=new AnswerApi();var answered=WebAnswerCompletion.Complete(answerApi,"test","https://example.org","test",new[]{new ChatTurn("user",fullEvidence)});
   Check(answerApi.Calls==2&&answerApi.Budgets[0]==8192&&answerApi.Budgets[1]==16384&&answered.Ok&&!answered.Truncated&&answerApi.LastInput.Contains("WIKI_END"),"truncated web answer retries with full original evidence and sufficient output budget");
   answerApi=new AnswerApi{AlwaysTruncated=true};answered=WebAnswerCompletion.Complete(answerApi,"test","https://example.org","test",new[]{new ChatTurn("user",fullEvidence)});
   Check(!answered.Ok&&answerApi.Calls==2,"repeated truncation reports incomplete instead of saving a half workflow");
   var researchApi=new ResearchApi(outcome);var researchSlot=ArchiveStore.NewSlot("deploy");ArchiveStore.AddTurn(researchSlot,"answer",Share);
   var research=ArchiveResearch.Ask(researchApi,new PetState{WebEnabled=true},"test",ArchiveInterview.BuildQuestion(researchSlot),ArchiveResearch.Context(researchSlot),new List<string>{Share});
   Check(researchApi.LastInput.Contains("FIRST_END")&&researchApi.LastInput.Contains("SECOND_END")&&research.Resources.Count==3,"interview receives both files and caches separate resource cards");
   var slot=ArchiveStore.NewSlot("isolated");DeepSeekShare.Cache(slot,outcome);DeepSeekShare.Cache(slot,outcome);
   Check(slot.Resources.Count==2&&slot.Items.Count==0&&slot.Workflows.Count==0&&slot.Resources[0].Content.Contains("kv_producer"),"cache deduplicates attachment cards without upgrading skills");
   string prior=slot.Resources[0].Content;outcome.Attachments[0].Text="";outcome.Attachments[0].Status="未读取：签名过期";DeepSeekShare.Cache(slot,outcome);
   Check(slot.Resources[0].Content==prior&&slot.Resources[0].Note.Contains("保留上次缓存"),"failed refresh preserves old content with explicit stale note");
   var repo=new JsonRepository<ArchiveBook>(Path.Combine(root,"attachment-roundtrip.json"));repo.Save(new ArchiveBook{Slots=new List<ArchiveSlot>{slot},ActiveId=slot.Id});
   Check(repo.Load().Slots[0].Resources[1].Content.Contains("SECOND_END"),"attachment content survives restart");
   foreach(string bad in new[]{"http://chat.deepseek.com/share/testshare12345678","https://chat.deepseek.com.attacker.org/share/testshare12345678","https://u:p@chat.deepseek.com/share/testshare12345678","https://chat.deepseek.com/share/../admin","https://chat.deepseek.com/share/testshare12345678?key=secret"})Check(!DeepSeekShare.CanRead(bad),"reject invalid share address");
   foreach(string bad in new[]{"https://evil.example/api/file?file_id=f&state=s","//evil.example/file?file_id=f&state=s","https://files.deepseeksvc.com:444/api/file?file_id=f&state=s","https://files.deepseeksvc.com/api/other?file_id=f&state=s","/file?file_id=f"})Check(DeepSeekShare.AttachmentUrl(bad)=="","reject unknown file destinations or missing capability");
   download=new Download(ShareJson(FileJson("file-one","prefill.txt",first.Length),FileJson("file-two","decode.txt",second.Length)),second);
   outcome=DeepSeekShare.Fetch(Share+"#attachment-file-two",30,download);
   Check(outcome.Attachments.Count==1&&outcome.Attachments[0].Text.Contains("SECOND_END")&&download.Urls.Count==2,"refreshing a specific attachment reads only that file");
   download=new Download(ShareJson(FileJson("file-one","prefill.txt",first.Length)),first);
   Check(!DeepSeekShare.Fetch(Share+"#attachment-file-missing",30,download).Ok&&download.Urls.Count==1,"removed attachment reports missing instead of substituting page text");
   download=new Download(ShareJson(FileJson("f","private.sh",10),FileJson("p","paper.pdf",10),FileJson("x","huge.txt",DeepSeekShare.MaxFileBytes+1)));
   outcome=DeepSeekShare.Fetch(Share,30,download);
   Check(outcome.Ok&&download.Urls.Count==1&&outcome.Attachments.All(a=>a.Text==""),"unsupported unaudited and oversized files are not downloaded");
   Check(outcome.AttachmentStatus.Contains("未读取 3"),"partial availability is explicit even when page read succeeds");
   download=new Download(ShareJson(FileJson("f","code.sh",first.Length,",\"audit_result\":\"pass\"")),first);outcome=DeepSeekShare.Fetch(Share,30,download);
   Check(outcome.Attachments[0].Text.Contains("FIRST_END"),"audited text code attachments are supported");
   download=new Download(ShareJson(FileJson("f","file.txt",5)),Encoding.UTF8.GetBytes("<!doctype html>"));outcome=DeepSeekShare.Fetch(Share,30,download);
   Check(outcome.Attachments[0].Text==""&&outcome.Attachments[0].Status.Contains("大小不符"),"wrong-size or HTML responses cannot masquerade as files");
   download=new Download(ShareJson(FileJson("f","file.txt",first.Length)),first){Html=true};outcome=DeepSeekShare.Fetch(Share,30,download);
   Check(outcome.Attachments[0].Text=="","HTML content type is rejected independently of extension");
   var many=new List<string>();for(int i=0;i<8;i++)many.Add(FileJson("f"+i,"code.txt",1));download=new Download(ShareJson(many.ToArray()),Enumerable.Repeat(new byte[]{65},6).ToArray());outcome=DeepSeekShare.Fetch(Share,30,download);
   Check(download.Urls.Count==7&&outcome.Attachments.Count(a=>a.Text.Length>0)==6,"attachment request count is bounded");
   byte[] large=Encoding.UTF8.GetBytes(new string('x',21000));download=new Download(ShareJson(FileJson("f","large.txt",large.Length)),large);outcome=DeepSeekShare.Fetch(Share,30,download);
   Check(outcome.Attachments[0].Text.Length<=20000&&outcome.Attachments[0].Status.StartsWith("部分读取")&&outcome.Content.Contains("节选"),"cache and prompt truncation are both labeled");
   foreach(string invalid in new[]{"not json","{\"code\":401}","{\"code\":0,\"data\":{\"biz_code\":404}}"}) {download=new Download(invalid);Check(!DeepSeekShare.Fetch(Share,30,download).Ok,"invalid revoked or unavailable share fails honestly");}
   var utf16=new byte[]{255,254}.Concat(Encoding.Unicode.GetBytes("中文 UTF16")).ToArray();Check(DeepSeekShare.Decode(utf16,"application/octet-stream")=="中文 UTF16","UTF16 BOM supported");
   Check(DeepSeekShare.Decode(Encoding.GetEncoding(54936).GetBytes("中文 GB18030"),"text/plain")=="中文 GB18030","GB18030 fallback supported");
   bool rejected=false;try{DeepSeekShare.Decode(new byte[]{65,0,66},"application/octet-stream");}catch(InvalidDataException){rejected=true;}Check(rejected,"binary file disguised as text rejected");
   Console.WriteLine("PASS share attachment: "+checks+" checks");
  }
  sealed class AnswerApi:IArchiveApi {
   public int Calls;public bool AlwaysTruncated;public List<int> Budgets=new List<int>();public string LastInput;
   public ChatOutcome Complete(string key,string url,string model,bool thinking,ChatTurn[] turns,int timeout,int tokens,double temperature){Calls++;Budgets.Add(tokens);LastInput=string.Join("\n",turns.Select(t=>t.Content).ToArray());return new ChatOutcome{Ok=true,Text="流程结果",Truncated=AlwaysTruncated||Calls==1};}
   public WebOutcome Search(string a,string b,string c){throw new Exception("must not refetch");}public WebOutcome Fetch(string a,string b,string c){throw new Exception("must not refetch");}
  }
  sealed class Download:IPublicDownloader {
   readonly string json;readonly Queue<byte[]> bodies;public bool Html;public readonly List<string> Urls=new List<string>();
   public Download(string json,params byte[][] bodies){this.json=json;this.bodies=new Queue<byte[]>(bodies);}
   public PublicDownload Get(string url,int limit,int timeoutMs){Urls.Add(url);byte[] bytes=Urls.Count==1?Encoding.UTF8.GetBytes(json):bodies.Dequeue();if(bytes.Length>limit)throw new InvalidDataException();return new PublicDownload{Bytes=bytes,ContentType=Urls.Count==1?"application/json":Html?"text/html":"application/octet-stream"};}
  }
  sealed class ResearchApi:IArchiveApi {
   readonly WebOutcome result;int count;public string LastInput="";
   public ResearchApi(WebOutcome result){this.result=result;}
   public ChatOutcome Complete(string key,string url,string model,bool thinking,ChatTurn[] turns,int timeout,int tokens,double temperature){LastInput=turns.Last().Content;return new ChatOutcome{Ok=true,Text=++count==1?"{\"mode\":\"read\",\"url\":\""+Share+"\"}":AssessmentTests.Question("你如何配置这两个节点？")};}
   public WebOutcome Search(string key,string url,string query){throw new Exception("Unexpected search");}
   public WebOutcome Fetch(string key,string url,string page){return result;}
  }
 }
}
