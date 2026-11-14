using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
namespace ElymsPet {
 public static class ClipboardEditing {
  [DllImport("user32.dll")]static extern bool OpenClipboard(IntPtr owner);
  [DllImport("user32.dll")]static extern bool CloseClipboard();
  [DllImport("user32.dll")]static extern bool EmptyClipboard();
  [DllImport("user32.dll")]static extern IntPtr SetClipboardData(uint format,IntPtr data);
  [DllImport("kernel32.dll")]static extern IntPtr GlobalAlloc(uint flags,UIntPtr size);
  [DllImport("kernel32.dll")]static extern IntPtr GlobalLock(IntPtr handle);
  [DllImport("kernel32.dll")]static extern bool GlobalUnlock(IntPtr handle);
  [DllImport("kernel32.dll")]static extern IntPtr GlobalFree(IntPtr handle);
  public static bool Write(string text,IntPtr owner){
   if(owner==IntPtr.Zero)return false;byte[] bytes=Encoding.Unicode.GetBytes(text+"\0");IntPtr data=GlobalAlloc(0x42,(UIntPtr)bytes.Length);if(data==IntPtr.Zero)return false;
   try{IntPtr address=GlobalLock(data);if(address==IntPtr.Zero)return false;try{Marshal.Copy(bytes,0,address,bytes.Length);}finally{GlobalUnlock(data);}
    bool opened=false;for(int i=0;i<5&&!opened;i++){opened=OpenClipboard(owner);if(!opened)System.Threading.Thread.Sleep(20);}if(!opened)return false;
    try{if(!EmptyClipboard()||SetClipboardData(13,data)==IntPtr.Zero)return false;data=IntPtr.Zero;return true;}finally{CloseClipboard();}
   }finally{if(data!=IntPtr.Zero)GlobalFree(data);}
  }
  public static bool Cut(TextBox box,Func<string,bool> write){
   if(box.IsReadOnly||!box.IsEnabled||box.SelectionLength==0)return false;
   string selected=box.SelectedText;if(!write(selected))return false;
   box.BeginChange();try{box.SelectedText="";}finally{box.EndChange();}return true;
  }
  public static void Attach(TextBox box,Func<string,bool> writer=null){
   box.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut,delegate(object sender,ExecutedRoutedEventArgs e){
    bool ok=Cut(box,writer??delegate(string value){var window=Window.GetWindow(box);return window!=null&&Write(value,new WindowInteropHelper(window).Handle);});e.Handled=true;
    if(!ok&&box.SelectionLength>0){var tip=new ToolTip{PlacementTarget=box,Content="剪贴板暂时被占用，输入已保留，请重试",IsOpen=true};var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};timer.Tick+=delegate{timer.Stop();tip.IsOpen=false;};timer.Start();}
   },delegate(object sender,CanExecuteRoutedEventArgs e){e.CanExecute=!box.IsReadOnly&&box.IsEnabled&&box.SelectionLength>0;e.Handled=true;}));
  }
 }
}
