using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
namespace ElymsPet {
 static class SourceResearchTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Source research: "+name);Console.WriteLine("PASS source research: "+name);}
  static bool Reject(string payload){try{bool repaired;SourceResearch.ParseRequest(payload,out repaired);return false;}catch{return true;}}
  static string Git(string folder,string args){using(var p=Process.Start(new ProcessStartInfo("git.exe",args){WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){string text=p.StandardOutput.ReadToEnd();if(!p.WaitForExit(10000)||p.ExitCode!=0)throw new Exception("git fixture failed");return text.Trim();}}
  public static void Run(string root){
   string repo=Path.Combine(root,"research 源码 with spaces"),previous=Storage.Folder;Directory.CreateDirectory(repo);
   try{
    Storage.Folder=Path.Combine(root,"research-storage");Git(repo,"init");Git(repo,"config core.autocrlf false");
    File.WriteAllText(Path.Combine(repo,"engine.py"),"def step():\n    return iteration_details\n");
    File.WriteAllText(Path.Combine(repo,"logger.py"),"def record(iteration_details):\n    print(iteration_details.elapsed_ms)\n");
    Directory.CreateDirectory(Path.Combine(repo,"tests"));var noisy=new System.Text.StringBuilder();for(int i=0;i<80;i++)noisy.Append("# iteration_details fixture\n");File.WriteAllText(Path.Combine(repo,"tests","fixture.py"),noisy.ToString());
    Git(repo,"add .");Git(repo,"-c user.name=Fixture -c user.email=fixture@example.invalid commit -m fixture");string commit=Git(repo,"rev-parse HEAD");
    string url="https://github.com/org/repo/blob/"+commit+"/engine.py#L1";
    var settings=new LocalRepositorySettings{Repositories=new List<LocalRepositoryMapping>{new LocalRepositoryMapping{Repository="org/repo",Folder=repo}}};
    Func<SourceResearch> create=()=>new SourceResearch(settings,new[]{url});
    Func<string,SourceQuery> search=word=>new SourceQuery{Repository="org/repo",Commit=commit,Action="search",Query=word};
    Func<string,int,SourceQuery> read=(path,line)=>new SourceQuery{Repository="org/repo",Commit=commit,Action="read",Path=path,Line=line};
    string valid=GraphEditing.Json(new SourceQueries{Queries=new List<SourceQuery>{search("iteration_details")}});bool repaired;
    var parsed=SourceResearch.ParseRequest(valid,out repaired);Check(!repaired&&parsed.Queries[0].Query=="iteration_details","valid payload needs no repair");
    foreach(string malformed in new[]{valid.Substring(0,valid.Length-1),valid.Substring(0,valid.Length-1)+"]",valid+"]"}){
     parsed=SourceResearch.ParseRequest(malformed,out repaired);Check(repaired&&GraphEditing.Json(parsed)==valid,"observed outer closing-bracket defect is repaired without changing query values");
    }
    foreach(string malformed in new[]{valid+"{}",valid+"trailing text",valid.Substring(0,valid.Length-3),"{\"Queries\":[{\"Query\":\"unfinished}]",valid.Replace("iteration_details","value}]}] text")+"suffix"})Check(Reject(malformed),"truncated values, inner objects and appended data are rejected");
    Check(!SourceResearch.SyntaxShape("{\"Query\":\"private\\\"value\",\"Line\":123}").Contains("private")&&!SourceResearch.SyntaxShape("{\"Query\":\"private\\\"value\",\"Line\":123}").Contains("123"),"format diagnostics retain delimiters without string or numeric values");
    var tool=create();string found=tool.Execute(search("iteration_details"));Check(tool.Available&&found.Contains("engine.py:2:")&&found.Contains("logger.py:2:"),"search finds producers and consumers across files");
    File.WriteAllText(Path.Combine(repo,"logger.py"),"UNCOMMITTED SECRET");
    string evidence=tool.Execute(read("logger.py",1));Check(evidence.Contains("L1: def record")&&evidence.Contains(commit+"/logger.py#L1")&&!evidence.Contains("SECRET"),"reads pinned Git objects with line attribution despite dirty working tree");
    Check(tool.Execute(read("logger.py",1)).Contains("相同查询"),"duplicate reads do not repeat Git work");
    Check(found.IndexOf("logger.py:2:",StringComparison.Ordinal)<found.IndexOf("tests/fixture.py:",StringComparison.Ordinal)&&found.Contains("截断"),"runtime consumers stay visible when test hits exceed search cap");
    Check(create().Execute(read("logger.py",2)).Contains("前置顶层声明")&&create().Execute(read("logger.py",2)).Contains("L1: def record"),"mid-function reads include preceding declaration instead of requiring guessed ownership");
    Check(tool.Execute(read("../outside.py",1)).Contains("未完成")&&tool.Execute(read("logger.py",900)).Contains("超出"),"traversal and out of range reads fail explicitly");
    var bad=search("iteration_details");bad.Commit=new string('0',40);Check(tool.Execute(bad).Contains("不在本轮范围"),"unapproved commits are rejected");
    bad=search("iteration_details");bad.Repository="other/repo";Check(tool.Execute(bad).Contains("不在本轮范围"),"unmapped repositories are rejected");
    Check(tool.Execute(search("foo\" & whoami")).Contains("搜索词"),"shell metacharacters are rejected");
    Check(!new SourceResearch(settings,new[]{url.Replace(commit,"main")}).Available,"moving branch cannot enable tool");
    Check(create().Execute(search("unknown_symbol")).Contains("没有命中"),"empty search is distinguished from read failure");
    int round=0;var result=create().Complete(new[]{new ChatTurn("system","rules"),new ChatTurn("user","迭代统计在哪里使用？")},input=>{
     round++;
     if(round==1){Check(input[input.Length-1].Content.Contains("只读源码工具"),"model is told how to request source tools");return Request(search("iteration_details"));}
     if(round==2){Check(input[input.Length-1].Content.Contains("logger.py:2:"),"search evidence reaches next model round");return Request(read("logger.py",1));}
     Check(input[input.Length-1].Content.Contains("L2:     print"),"consumer code reaches final answer round");return new ChatOutcome{Ok=true,Text="record 读取 iteration_details.elapsed_ms。",Seconds=1,CompletionTokens=10};
    });
    Check(result.Ok&&round==3&&result.Text.Contains("record")&&!result.Text.Contains("SOURCE_READ")&&result.CompletionTokens==30&&result.Seconds==3,"tool rounds yield a clean final answer and aggregate cost");
    int calls=0;result=create().Complete(new ChatTurn[0],input=>{calls++;return Request(search("iteration_details"));});Check(!result.Ok&&calls==4&&result.Error.Contains("上限"),"repeated requests terminate at bounded round budget");
    calls=0;result=create().Complete(new ChatTurn[0],input=>{calls++;if(calls==1)return new ChatOutcome{Ok=true,Text="[[SOURCE_READ]]"+valid.Substring(0,valid.Length-1)+"][[/SOURCE_READ]]"};if(calls==2){Check(input[input.Length-2].Content=="[[SOURCE_READ]]"+valid+"[[/SOURCE_READ]]"&&input[input.Length-1].Content.Contains("logger.py:2:"),"repaired request executes and only canonical JSON enters subsequent model context");return Request(read("logger.py",1));}return new ChatOutcome{Ok=true,Text="record reads the field"};});Check(result.Ok&&calls==3,"outer wrapper error does not waste a model repair round or stop source reading");
    result=create().Complete(new ChatTurn[0],input=>new ChatOutcome{Ok=true,Text="[[SOURCE_READ]]{}",Truncated=true});Check(!result.Ok&&result.Error.Contains("截断"),"truncated tool request is never displayed as an answer");
    result=create().Complete(new ChatTurn[0],input=>new ChatOutcome{Ok=true,Text="[[SOURCE_READ]]{}"});Check(!result.Ok,"incomplete marker fails without leaking protocol");
    calls=0;result=create().Complete(new ChatTurn[0],input=>{calls++;if(calls==1)return new ChatOutcome{Ok=true,Text="[[SOURCE_READ]]{broken}[[/SOURCE_READ]]"};if(calls==2)Check(!input[input.Length-2].Content.Contains("{broken}")&&input[input.Length-1].Content.Contains("Queries"),"invalid request is replaced by actionable format feedback without reinforcing malformed JSON");if(calls<5)return Request(search("iteration_details"));return new ChatOutcome{Ok=true,Text="done"};});Check(result.Ok&&calls==5,"one format repair preserves three useful tool rounds and stays bounded");
    calls=0;result=create().Complete(new ChatTurn[0],input=>{calls++;return new ChatOutcome{Ok=true,Text="[[SOURCE_READ]]{broken}[[/SOURCE_READ]]"};});Check(!result.Ok&&calls==2&&result.Error.Contains("连续错误"),"irreparable consecutive errors still fail with a bounded retry");
    Check(Directory.GetFiles(Path.Combine(Storage.Folder,"source-research"),"*.jsonl").Length==1,"source queries have local diagnostic traces");
    string prompt=RepositoryLearning.Prompt(RepositoryLearningExample.Create(),RepositoryLearningExample.Create().Nodes[0],"解释");
    Check(prompt.Contains("不主动逐个翻译")&&!prompt.Contains("再给一个可验证的小问题")&&!prompt.Contains("你不能自行访问网络或本机仓库"),"code prompt removes forced translations, quizzes and obsolete access denial");
   }finally{Storage.Folder=previous;foreach(string path in Directory.GetFiles(repo,"*",SearchOption.AllDirectories))File.SetAttributes(path,FileAttributes.Normal);}
  }
  static ChatOutcome Request(SourceQuery query){return new ChatOutcome{Ok=true,Text="[[SOURCE_READ]]"+GraphEditing.Json(new SourceQueries{Queries=new List<SourceQuery>{query}})+"[[/SOURCE_READ]]",Seconds=1,CompletionTokens=10};}
 }
}
