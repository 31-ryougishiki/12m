using System;
using System.IO;
using System.Runtime.Serialization;
namespace ElymsPet {
 [DataContract] public sealed class DshKnowledgeFile {
  [DataMember] public string label;
  [DataMember] public string path;
 }
 [DataContract] public sealed class DshKnowledgeManifest {
  [DataMember] public string archive;
  [DataMember] public string root;
  [DataMember] public DshKnowledgeFile[] files;
  public static DshKnowledgeManifest Create(ArchiveSlot slot) {
   if(slot==null)throw new ArgumentNullException("slot");
   string folder=Path.GetFullPath(ArchiveKnowledge.Folder(slot));
   var names=new[]{"SKILL.md","workflows.md","graph.json","profile.md","episodes.md","references/代码仓/index.md","references/论文/index.md","references/文档/index.md","references/其他/index.md","GRAPH_EDIT.md"};
   var labels=new[]{"已确认流程与使用边界","全部流程（含待核实草稿）","知识图谱（领域、节点、关系、版本）","知识背景与掌握依据","案例与结果","代码仓资料索引","论文资料索引","文档资料索引","其他资料索引","编辑知识图谱的协议与结果确认"};
   var entries=new DshKnowledgeFile[names.Length];
   for(int i=0;i<names.Length;i++)entries[i]=new DshKnowledgeFile{label=labels[i],path=Path.Combine(folder,names[i].Replace('/',Path.DirectorySeparatorChar))};
   return new DshKnowledgeManifest{archive=slot.Name,root=folder,files=entries};
  }
 }
 public sealed partial class ArchiveService {
  public DshKnowledgeManifest PrepareDshKnowledge() {
   if(preview)return null;
   ArchiveKnowledge.WriteFiles(slot);
   return DshKnowledgeManifest.Create(slot);
  }
 }
}
