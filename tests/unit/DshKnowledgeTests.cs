using System;
using System.IO;
namespace ElymsPet {
 static class DshKnowledgeTests {
  public static void Run(string root) {
   string previous=Storage.Folder;Storage.Folder=Path.Combine(root,"dsh-knowledge");
   try {
    var book=ArchiveStore.Normalize(new ArchiveBook());var slot=book.Slots[0];
    slot.Name="测试 / 资料";slot.Workflows.Add(new ArchiveWorkflow{Title="示例草稿",Steps="1. 启动\n2. 测试"});
    ArchiveKnowledge.WriteFiles(slot);var manifest=DshKnowledgeManifest.Create(slot);
    if(manifest.files.Length!=10)throw new Exception("Missing DSH knowledge references");
    foreach(var entry in manifest.files)if(!Path.IsPathRooted(entry.path)||!File.Exists(entry.path)||!entry.path.StartsWith(manifest.root+Path.DirectorySeparatorChar))throw new Exception("Invalid knowledge reference");
    string workflow=File.ReadAllText(Path.Combine(manifest.root,"workflows.md"));
    if(!workflow.Contains("草稿")||File.ReadAllText(Path.Combine(manifest.root,"SKILL.md")).Contains("## 示例草稿"))throw new Exception("Draft lost execution boundary");
    slot.Workflows[0].Steps="1. 修订后测试";ArchiveKnowledge.WriteFiles(slot);
    if(!File.ReadAllText(Path.Combine(manifest.root,"workflows.md")).Contains("修订后测试"))throw new Exception("Stale handoff content");
    var other=new ArchiveSlot{Id="another",Name="另一个存档"};
    if(DshKnowledgeManifest.Create(other).root==manifest.root)throw new Exception("Archive switch must change knowledge directory");
    string prompt=Persona.SystemPrompt(new PetState(),DateTime.Now,"","检测到 DSH 工作台入口，工作区：测试。 委托自动附带流程路径。");
    if(!prompt.Contains("[[DSH_TASK]]")||!prompt.Contains("自动附带流程路径"))throw new Exception("Knowledge delegation unavailable to persona");
    Console.WriteLine("PASS DSH knowledge: generated paths exist, drafts stay drafts, refreshed content, archive isolation and persona delegation");
   } finally {Storage.Folder=previous;}
  }
 }
}

