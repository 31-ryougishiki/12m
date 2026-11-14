using System;
using System.Collections.Generic;
namespace ElymsPet {
 static class RepositoryLearningTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Repository learning: "+name);Console.WriteLine("PASS learning: "+name);}
  public static void Run(){
   var example=RepositoryLearningExample.Create();var node=example.Nodes[0];
   foreach(var n in example.Nodes)Check(RepositoryLearning.PinnedCode(n.Learning.CodeUrl)&&n.Code.Length>0,"example has pinned line and actual excerpt: "+n.Id);
   Check(!RepositoryLearning.PinnedCode("https://github.com/a/b/blob/main/a.py#L2"),"moving branch is not pinned evidence");
   foreach(string url in new[]{"javascript:alert(1)","file:///c:/foo","http://example.com","https://user:password@example.com"})Check(!RepositoryLearning.SafeUrl(url),"unsafe source rejected");
   string anchor=node.Learning.CodeUrl;var legacy=new DomainNode{Sources="说明 [源码]("+anchor+")\n"+anchor+"\nhttps://github.com/org/repo/blob/main/a.py#L1"};
   Check(RepositoryLearning.CodeLocations(legacy).Count==1&&RepositoryLearning.CodeLocations(legacy)[0]==anchor,"legacy Sources gains code navigation without migration and deduplicates links");
   Check(RepositoryLearning.CodeLocationLabel(anchor).Contains("scheduler.py #L685"),"code link shows file and exact line");
   legacy.Learning=new NodeLearning{CodeUrl=anchor.Replace("#L685","#L686")};Check(RepositoryLearning.CodeLocations(legacy)[0].EndsWith("#L686")&&RepositoryLearning.CodeLocations(legacy).Count==2,"explicit code location precedes legacy sources");
   Check(RepositoryLearning.Prompt(example,legacy,"解释").Contains(anchor),"tutor receives legacy source locations");
   Check(RepositoryLearning.CodeLocations(new DomainNode{Sources="只有说明，没有链接"}).Count==0,"no fabricated navigation without an anchor");
   var good=new LearningEvidence{Title="source",Url="https://example.org/source",Excerpt="原文依据",Reason="当前节点的关联",Version=node.Version,Reviewed=true};
   node.Learning.Evidence.Add(good);Check(RepositoryLearning.SelectedEvidence(node).Count==1,"complete manually reviewed evidence admitted");
   good.Reviewed=false;Check(RepositoryLearning.SelectedEvidence(node).Count==0,"unreviewed evidence hidden");good.Reviewed=true;
   good.Version="other";Check(RepositoryLearning.SelectedEvidence(node).Count==0,"version mismatch hidden even after review");good.Version=node.Version;
   good.Excerpt="";Check(RepositoryLearning.SelectedEvidence(node).Count==0,"empty excerpt hidden");good.Excerpt="原文依据";
   node.Learning.Evidence.Add(good);Check(RepositoryLearning.SelectedEvidence(node).Count==1,"duplicate citation collapsed");
   for(int i=0;i<5;i++)node.Learning.Evidence.Add(new LearningEvidence{Url="https://example.org/"+i,Excerpt="e",Reason="r",Version=node.Version,Reviewed=true});
   Check(RepositoryLearning.SelectedEvidence(node).Count==3,"default evidence budget is three");
   node.Learning.Reflection="我的解释";var clone=DomainKnowledge.Clone(example);Check(clone.Nodes[0].Learning.Reflection=="我的解释","learning survives persistence roundtrip");
   Check(RepositoryLearning.Prompt(example,node,"反馈").Contains("我的解释")&&RepositoryLearning.Prompt(example,node,"反馈").Contains(node.Learning.CodeUrl),"tutor gets reflection and pinned code");
   Check(RepositoryLearning.Validate(new NodeLearning{CodeUrl="https://github.com/a/b/blob/main/x#L1"}).Length>0,"invalid code anchor rejected on save");
  }
 }
}
