using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
namespace ElymsPet {
 // One coordinator for every caller: startup never submits or replays a task.
 public sealed class DshStartupCoordinator {
  readonly object gate=new object();readonly Func<DshHealthResult> health;readonly Action launch;readonly Func<bool> running;readonly Action wait;readonly int timeoutMs;
  public DshStartupCoordinator(Func<DshHealthResult> health,Action launch,Func<bool> running,Action wait=null,int timeoutMs=45000){this.health=health;this.launch=launch;this.running=running;this.wait=wait??(()=>Thread.Sleep(250));this.timeoutMs=timeoutMs;}
  public DshHealthResult Ensure(Action<string> progress=null,Func<bool> cancelled=null){
   progress=progress??(s=>{});cancelled=cancelled??(()=>NetworkCancellation.IsCancellationRequested);var clock=Stopwatch.StartNew();bool entered=false;
   try{
    while(!(entered=Monitor.TryEnter(gate,100))){if(cancelled())return Failure("DSH 启动等待已取消。");if(clock.ElapsedMilliseconds>=timeoutMs)return Failure("等待 DSH 启动超时，请重试。");}
    if(cancelled())return Failure("DSH 启动等待已取消。");
    var result=health();if(result.Ok)return result;
    if(cancelled())return Failure("DSH 启动等待已取消。");
    if(!running()){progress("DSH 未运行，正在自动启动…");launch();}else progress("正在等待 DSH 桥接就绪…");
    while(clock.ElapsedMilliseconds<timeoutMs){
     if(cancelled())return Failure("DSH 启动等待已取消。");
     result=health();if(result.Ok)return result;
     if(!running())return Failure("DSH 启动后已退出，请检查 web 配置及 dsh-elyms-bridge 插件。");
     wait();
    }
    return Failure("DSH 已启动，但桥接在 45 秒内未就绪。请检查 web 配置中的 dsh-elyms-bridge 插件后重试；问题未提交。");
   }catch(FileNotFoundException e){return Failure(e.Message);}catch{return Failure("无法自动启动 DSH，请检查本机 Node.js、DSH 安装和 web 配置后重试。");}
   finally{if(entered)Monitor.Exit(gate);}
  }
  static DshHealthResult Failure(string message){return new DshHealthResult{Error=message};}
 }
 public static class DshStartup {
  static Process launched;
  static readonly DshStartupCoordinator coordinator=new DshStartupCoordinator(()=>DshApi.Health(1000),Launch,Running);
  public static DshHealthResult Ensure(Action<string> progress=null,Func<bool> cancelled=null){return coordinator.Ensure(progress,cancelled);}
  static bool Running(){
   if(launched!=null){try{if(!launched.HasExited)return true;}catch{}launched.Dispose();launched=null;}
   string error;var handshake=DshApi.ReadHandshake(out error);return handshake!=null&&handshake.pid>0;
  }
  public static ProcessStartInfo LaunchInfo(){
   var folders=new List<string>();foreach(string entry in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){string path=entry.Trim().Trim('"');if(Path.IsPathRooted(path))folders.Add(path);}
   folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"npm"));
   string node="",script="";foreach(string folder in folders){if(node.Length==0&&File.Exists(Path.Combine(folder,"node.exe")))node=Path.Combine(folder,"node.exe");string candidate=Path.Combine(folder,"node_modules","@deepseek-ai","dsh","lib","bin.js");if(script.Length==0&&File.Exists(candidate))script=candidate;}
   if(node.Length==0)throw new FileNotFoundException("未找到 Node.js，无法自动启动 DSH。请安装 Node.js 并重启曳尔沐。");
   if(script.Length==0)throw new FileNotFoundException("未找到 @deepseek-ai/dsh，请安装 DSH CLI 并配置 web profile 的 dsh-elyms-bridge 插件。");
   string home=Environment.GetEnvironmentVariable("DSH_HOME");if(string.IsNullOrWhiteSpace(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".dsh");
   if(!Directory.Exists(Path.Combine(home,"profiles","web")))throw new FileNotFoundException("未找到 DSH web 配置，请先配置 web profile 和 dsh-elyms-bridge 插件。");
   return new ProcessStartInfo(node,"\""+script+"\" --profile web"){WorkingDirectory=home,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
  }
  static void Launch(){
   var process=new Process{StartInfo=LaunchInfo()};
   // Drain both streams; DSH logs can contain authenticated URLs, so do not persist them.
   process.OutputDataReceived+=delegate{};process.ErrorDataReceived+=delegate{};
   try{process.Start();launched=process;process.BeginOutputReadLine();process.BeginErrorReadLine();}catch{if(launched!=process)process.Dispose();throw;}
  }
 }
}
