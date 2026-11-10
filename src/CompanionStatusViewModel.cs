using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ElymsPet {
 public enum PanelTab { Companion, Archive, Workbench, Web, Bili, Focus, Notes, Settings, Character, Memory }

 /// <summary>陪伴面板的派生显示状态。动画刷新不负责驱动这些属性。</summary>
 public sealed class CompanionStatusViewModel : INotifyPropertyChanged {
  string connection,energy,friendship,focusCount,focusClock,pauseLabel;
  bool canPause;
  public string Connection {get{return connection;}}
  public string Energy {get{return energy;}}
  public string Friendship {get{return friendship;}}
  public string FocusCount {get{return focusCount;}}
  public string FocusClock {get{return focusClock;}}
  public string PauseLabel {get{return pauseLabel;}}
  public bool CanPause {get{return canPause;}}
  public event PropertyChangedEventHandler PropertyChanged;
  public void Refresh(PetState state,DateTime now) {
   Set(ref connection,state.Sleeping?"☾  休息中 · 连接仍在":"●  在线 · 信号满格","Connection");
   Set(ref energy,"能量缓存  "+((int)state.Energy)+"%","Energy");
   Set(ref friendship,"相处印记  "+state.Friendship,"Friendship");
   Set(ref focusCount,"完成专注  "+state.FocusSessions+" 段","FocusCount");
   double remaining=state.Remaining(now);
   Set(ref focusClock,TimeText.Short(remaining>0?remaining:state.FocusMinutes*60),"FocusClock");
   Set(ref pauseLabel,state.PausedSeconds>0?"继续计时":"暂停计时","PauseLabel");
   Set(ref canPause,remaining>0,"CanPause");
  }
  void Set<T>(ref T field,T value,string name) {
   if(EqualityComparer<T>.Default.Equals(field,value))return;
   field=value;
   if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs(name));
  }
 }
}
