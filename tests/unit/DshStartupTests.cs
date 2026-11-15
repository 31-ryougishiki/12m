using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace ElymsPet {
 static class DshStartupTests {
  static void Check(bool ok,string name){if(!ok)throw new Exception("DSH startup: "+name);Console.WriteLine("PASS DSH startup: "+name);}
  static DshHealthResult Ready(bool value){return new DshHealthResult{Ok=value};}
  public static void Run(){
   int launches=0,polls=0,waits=0;bool running=false,cancelled=false;
   var coordinator=new DshStartupCoordinator(()=>Ready(true),()=>launches++,()=>false);
   Check(coordinator.Ensure().Ok&&launches==0,"healthy bridge is reused without launching");
   coordinator=new DshStartupCoordinator(()=>Ready(++polls>=4),()=>{launches++;running=true;},()=>running,()=>waits++);
   string progress="";Check(coordinator.Ensure(s=>progress=s).Ok&&launches==1&&waits==2&&progress.Contains("自动启动"),"cold startup waits for authenticated health before continuing");
   launches=0;polls=0;coordinator=new DshStartupCoordinator(()=>Ready(++polls>=3),()=>launches++,()=>true,()=>{});
   Check(coordinator.Ensure().Ok&&launches==0,"running but warming bridge is awaited without duplicate process");
   coordinator=new DshStartupCoordinator(()=>Ready(false),()=>launches++,()=>false);
   Check(!coordinator.Ensure(null,()=>true).Ok&&launches==0,"cancelled question never starts DSH");
   running=false;coordinator=new DshStartupCoordinator(()=>Ready(false),()=>{launches++;running=true;},()=>running,()=>cancelled=true);
   var result=coordinator.Ensure(null,()=>cancelled);Check(!result.Ok&&result.Error.Contains("取消")&&launches==1,"cancel during startup stops waiting without submitting");
   coordinator=new DshStartupCoordinator(()=>Ready(false),()=>{throw new FileNotFoundException("DSH CLI missing");},()=>false);
   Check(coordinator.Ensure().Error.Contains("DSH CLI missing"),"missing installation is actionable and does not report success");
   coordinator=new DshStartupCoordinator(()=>Ready(false),()=>{},()=>false);
   Check(coordinator.Ensure().Error.Contains("退出"),"early process exit is reported");
   launches=0;coordinator=new DshStartupCoordinator(()=>Ready(false),()=>launches++,()=>true,()=>Thread.Sleep(1),5);
   Check(coordinator.Ensure().Error.Contains("未就绪")&&launches==0,"bridge startup timeout is bounded");
   launches=0;polls=0;running=false;
   coordinator=new DshStartupCoordinator(()=>Ready(Interlocked.Increment(ref polls)>=4),()=>{Interlocked.Increment(ref launches);running=true;},()=>running,()=>Thread.Sleep(5));
   var first=Task.Factory.StartNew(()=>coordinator.Ensure());var second=Task.Factory.StartNew(()=>coordinator.Ensure());Task.WaitAll(first,second);
   Check(first.Result.Ok&&second.Result.Ok&&launches==1,"concurrent questions share a single startup");
  }
 }
}
