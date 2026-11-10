using System;
using System.IO;
namespace ElymsPet {
 static class ArchiveKnowledgeTests {
  static void Check(bool condition,string name) {if(!condition)throw new Exception("Archive: "+name);Console.WriteLine("PASS archive: "+name);}
  public static void Run(string root) {
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"knowledge-test");
   try {
    var book=ArchiveStore.Normalize(new ArchiveBook());var slot=ArchiveStore.Active(book);
    slot.Resources=null;slot.Workflows=null;ArchiveStore.Normalize(book);
    Check(slot.Resources!=null&&slot.Workflows!=null&&book.Version==4,"old archive migration");
    var result=ArchiveInterview.Parse("{\"focus\":\"部署\",\"pause\":true,\"items\":[{\"title\":\"回滚\",\"domain\":\"工程\",\"boundary\":\"尚未掌握\"}],\"resources\":[{\"title\":\"参考论文\",\"kind\":\"论文\",\"url\":\"file:///secret\"}],\"workflows\":[{\"title\":\"部署\",\"trigger\":\"发布版本\",\"preconditions\":\"测试通过\",\"steps\":\"1. 构建\\n2. 发布\",\"checks\":\"健康检查通过\",\"exceptions\":\"失败则回滚\",\"confirmed\":true}]}");
    ArchiveKnowledge.Merge(slot,result);ArchiveStore.Merge(slot,result.Items);
    Check(slot.Paused&&slot.Focus=="部署"&&slot.Items[0].Boundary=="尚未掌握","structured focus, pause and knowledge boundary");
    Check(slot.Resources[0].Url==""&&!slot.Workflows[0].Confirmed,"model cannot confirm workflows or supply file URLs");
    ArchiveKnowledge.WriteFiles(slot);string folder=ArchiveKnowledge.Folder(slot);
    Check(!File.ReadAllText(Path.Combine(folder,"SKILL.md")).Contains("健康检查通过"),"draft excluded from skill");
    var workflow=slot.Workflows[0];workflow.Confirmed=true;
    ArchiveKnowledge.WriteFiles(slot);
    Check(File.ReadAllText(Path.Combine(folder,"SKILL.md")).Contains("健康检查通过")&&ArchiveStore.Digest(slot,40,16000).Contains("失败则回滚"),"confirmed steps exported and injected");
    ArchiveKnowledge.Merge(slot,ArchiveInterview.Parse("{\"workflows\":[{\"title\":\"部署\",\"trigger\":\"新触发\",\"steps\":\"1. 修改\"}]}"));
    Check(!slot.Workflows[0].Confirmed,"revision invalidates confirmation");
    slot.Resources[0].Content="缓存的论文正文";slot.Resources[0].Retrieved=DateTime.UtcNow;
    Check(ArchiveStore.Save(book),"save v2");book=ArchiveStore.Load();slot=ArchiveStore.Active(book);
    Check(slot.Resources[0].Content=="缓存的论文正文"&&slot.Workflows.Count==1&&slot.Paused,"new fields survive reload");
    ArchiveKnowledge.WriteFiles(slot);
    Check(File.ReadAllText(Path.Combine(folder,"references","论文","index.md")).Contains("缓存的论文正文"),"categorized local content");
    ArchiveStore.Merge(slot,new[]{new ArchiveItem{Domain="工程",Title="回滚",Boundary="已掌握",Level="熟练",Evidence="已独立完成"}});
    ArchiveStore.Merge(slot,new[]{new ArchiveItem{Domain="工程",Title="回滚",Boundary="尚未掌握",Level="了解",Evidence="纠正：还不会"}});
    Check(slot.Items.Count==1&&slot.Items[0].Level=="了解"&&slot.Items[0].Boundary=="尚未掌握","correction can reduce mastery");
    slot.Resources.Clear();slot.Workflows.Clear();ArchiveKnowledge.WriteFiles(slot);
    Check(!File.ReadAllText(Path.Combine(folder,"references","论文","index.md")).Contains("缓存的论文正文")&&!File.ReadAllText(Path.Combine(folder,"SKILL.md")).Contains("健康检查通过"),"removed content cleared from generated files");
    File.WriteAllText(Path.Combine(folder,"user-note.txt"),"keep");ArchiveKnowledge.RemoveFiles(slot);
    Check(!File.Exists(Path.Combine(folder,"SKILL.md"))&&File.Exists(Path.Combine(folder,"user-note.txt")),"archive deletion cleans generated files and preserves attachments");
    Check(ArchiveInterview.BuildSystem(slot).Contains("未提及绝不当成不会")&&ArchiveInterview.BuildExtract(slot)[0].Content.Contains("workflows"),"both interview and extraction use workflow contract");
   } finally {Storage.Folder=previous;}
  }
 }
}
