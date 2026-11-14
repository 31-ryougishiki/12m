using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace ElymsPet {
 // Scale the layout as well as glyphs so fixed-height inputs and buttons remain readable.
 public sealed class ReadingZoom : ScrollViewer {
  readonly FrameworkElement page;
  readonly ScaleTransform transform=new ScaleTransform();
  readonly Action<double> changed;
  public double Factor {get{return transform.ScaleX;}}
  public static double Normalize(double value){return double.IsNaN(value)||double.IsInfinity(value)||value<=0?1:Math.Max(.8,Math.Min(2,value));}
  public ReadingZoom(Window window,FrameworkElement content,double initial,Action<double> onChanged){
   page=content;changed=onChanged;Content=page;page.LayoutTransform=transform;
   HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
   HorizontalContentAlignment=HorizontalAlignment.Left;VerticalContentAlignment=VerticalAlignment.Top;
   transform.ScaleX=transform.ScaleY=Normalize(initial);
   SizeChanged+=delegate{ResizePage();};
   window.PreviewMouseWheel+=OnWheel;window.PreviewKeyDown+=OnKey;
   window.Closed+=delegate{window.PreviewMouseWheel-=OnWheel;window.PreviewKeyDown-=OnKey;};
  }
  void ResizePage(){
   // Keep the page measured with a finite size (especially the chat's star-sized rows).
   page.Width=Math.Max(720,Math.Max(0,ActualWidth-SystemParameters.VerticalScrollBarWidth)/Factor);
   page.Height=Math.Max(480,Math.Max(0,ActualHeight-SystemParameters.HorizontalScrollBarHeight)/Factor);
  }
  public void SetFactor(double value){value=Math.Round(Normalize(value),2);if(Math.Abs(value-Factor)<.001)return;transform.ScaleX=transform.ScaleY=value;ResizePage();if(changed!=null)changed(value);}
  public bool HandleWheel(int delta,ModifierKeys modifiers){if((modifiers&ModifierKeys.Control)==0||delta==0)return false;SetFactor(Factor+(delta>0?.1:-.1));return true;}
  void OnWheel(object sender,MouseWheelEventArgs e){if(HandleWheel(e.Delta,Keyboard.Modifiers))e.Handled=true;}
  void OnKey(object sender,KeyEventArgs e){if((Keyboard.Modifiers&ModifierKeys.Control)!=0&&(e.Key==Key.D0||e.Key==Key.NumPad0)){SetFactor(1);e.Handled=true;}}
 }
}
