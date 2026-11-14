using System;

namespace ElymsPet {
 /// <summary>规划开始时捕获的输入，后台线程不会读取正在被设置界面修改的状态。</summary>
 public sealed class AutonomyRequest {
  public readonly PetState State;
  public readonly string Key, Profile, Recent, Last, DshContext, WebContext;
  public readonly DateTime Now;
  public AutonomyRequest(PetState state,string key,string profile,string recent,string last,
   string dshContext,string webContext,DateTime now) {
   State=state.Snapshot();Key=key;Profile=profile;Recent=recent;Last=last;
   DshContext=dshContext;WebContext=webContext;Now=now;
  }
  public bool DshAllowed {get{return PluginAccess.Enabled(State,"dsh")&&State.DshEnabled&&State.AutoLifeDsh&&State.DshAutoRun;}}
 }
}
