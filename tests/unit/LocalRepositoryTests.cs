using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
namespace ElymsPet {
 static class LocalRepositoryTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("Local source: "+name);Console.WriteLine("PASS local source: "+name);}
  static string Git(string directory,string arguments){using(var p=Process.Start(new ProcessStartInfo("git.exe",arguments){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){if(!p.WaitForExit(10000)){p.Kill();throw new Exception("fixture git timed out");}string output=p.StandardOutput.ReadToEnd();if(p.ExitCode!=0)throw new Exception(p.StandardError.ReadToEnd());return output.Trim();}}
  static bool Reject(Action action){try{action();return false;}catch{return true;}}
  public static void Run(string root){
   string repo=Path.Combine(root,"source 仓库 with spaces");Directory.CreateDirectory(repo);Git(repo,"init");Git(repo,"config core.autocrlf false");string file=Path.Combine(repo,"source.py");File.WriteAllText(file,"one\ntwo\nthree\n");Git(repo,"add source.py");Git(repo,"-c user.name=Fixture -c user.email=fixture@example.invalid commit -m fixture");string commit=Git(repo,"rev-parse HEAD");string url="https://github.com/org/repo/blob/"+commit+"/source.py#L2";
   var target=LocalRepository.Resolve(repo,url);Check(target.Exact&&target.Line==2&&target.File==file,"unchanged file resolves exact pinned line even in spaced Unicode path");
   var contextSettings=new LocalRepositorySettings{Repositories=new List<LocalRepositoryMapping>{new LocalRepositoryMapping{Repository="org/repo",Folder=repo}}};
   string context=LocalRepository.ReadContext(contextSettings,url);Check(context.Contains("已读取并核对")&&context.Contains("L2: two")&&context.Contains(commit)&&!context.Contains(repo),"AI receives verified numbered source bytes and portable attribution without local path");
   Check(LocalRepository.ReadContext(new LocalRepositorySettings(),url).Contains("尚未配置"),"unmapped source reports missing evidence");
   Check(LocalRepository.ReadContext(contextSettings,url.Replace("#L2","#L999")).Contains("行号超出"),"out-of-range source is not passed off as valid code");
   Check(LocalRepository.ReadContext(contextSettings,url.Replace("source.py","%2e%2e/outside.py")).Contains("未读取"),"context reader rejects traversal too");
   File.WriteAllText(file,"inserted\none\ntwo\nthree\n");target=LocalRepository.Resolve(repo,url);Check(!target.Exact&&target.Line==1,"dirty file does not pretend old line remains accurate");
   context=LocalRepository.ReadContext(contextSettings,url);Check(context.Contains("不一致")&&!context.Contains("inserted")&&!context.Contains("L2:"),"dirty bytes cannot leak into fixed-version source evidence");
   Check(Reject(delegate{LocalRepository.ResolveFile(repo,url.Replace("source.py","%2e%2e/outside.py"));}),"encoded traversal rejected");
   Check(Reject(delegate{LocalRepository.ResolveFile(repo,url.Replace("source.py","source.py%3Asecret"));}),"alternate stream rejected");
   Check(Reject(delegate{LocalRepository.ResolveFile(repo,url.Replace("source.py","missing.py"));}),"missing file is never created by editor");
   string fakeEditor=Path.Combine(repo,"Code.exe");File.WriteAllText(fakeEditor,"");var launch=LocalRepository.OpenInfo(fakeEditor,target);Check(!launch.UseShellExecute&&launch.Arguments.Contains("--goto \"")&&launch.Arguments.Contains("source.py:1:1\""),"editor receives quoted direct goto argument without shell");
   Check(Reject(delegate{LocalRepository.OpenInfo(file,target);}),"arbitrary executable is not accepted as supported editor");
   var longSource=new System.Text.StringBuilder();for(int i=1;i<=250;i++)longSource.Append("source_line_").Append(i).Append(new string('x',180)).Append('\n');File.WriteAllText(file,longSource.ToString(),new System.Text.UTF8Encoding(false));Git(repo,"add source.py");Git(repo,"-c user.name=Fixture -c user.email=fixture@example.invalid commit -m bounded");string longUrl=url.Replace(commit,Git(repo,"rev-parse HEAD")).Replace("#L2","#L100-L240");
   context=LocalRepository.ReadContext(contextSettings,longUrl);Check(context.Length<7200&&context.Contains("L100: source_line_100")&&!context.Contains("source_line_240")&&context.Contains("截断"),"long source ranges retain the target line and enforce a bounded excerpt");
   string previous=Storage.Folder;try{Storage.Folder=Path.Combine(root,"local-mapping");var settings=new LocalRepositorySettings{Editor=fakeEditor,Repositories=new List<LocalRepositoryMapping>{new LocalRepositoryMapping{Repository="org/repo",Folder=repo}}};string error;Check(LocalRepository.Save(settings,out error)&&LocalRepository.MappedFolder(LocalRepository.Load(),url)==repo,"local mapping persists outside cloud archive");}finally{Storage.Folder=previous;}
   string prose="先执行 `a；b`；再处理结果。关键结论：跨进程。";var parts=LearningReadability.Parts(prose);Check(parts.Count==3&&parts[0].Contains("`a；b`")&&string.Concat(parts.ToArray())==prose,"reading chunks preserve code punctuation and original wording");
   Check(LearningReadability.Parts("").Count==0,"empty approach handled");
   // Git stores objects as read-only on Windows; permit the harness to remove this fixture.
   foreach(string fixtureFile in Directory.GetFiles(repo,"*",SearchOption.AllDirectories))File.SetAttributes(fixtureFile,FileAttributes.Normal);
  }
 }
}
