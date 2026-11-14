using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace ElymsPet {
 static class RadialMenuTests {
  static void Check(bool ok,string label){if(!ok)throw new Exception("Radial menu: "+label);Console.WriteLine("PASS radial: "+label);}
  public static void Run(PetWindow pet){
   Check(RadialMenuLayout.Normalize(null)==RadialMenuLayout.DefaultPages,"legacy settings receive five default entries");
   Check(RadialMenuLayout.Normalize("Unknown,Archive,Archive")=="Archive","unknown and duplicate entries are normalized");
   Check(RadialMenuLayout.Normalize("")=="","all optional sectors can be hidden without restoring defaults");
   var state=new PetState{RadialPages="Companion,Notes",DomainGraphEntryAdded=true};
   // Serialization through memory avoids writing preview/user data.
   var serializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(PetState));
   using(var stream=new MemoryStream()){serializer.WriteObject(stream,state);stream.Position=0;var saved=(PetState)serializer.ReadObject(stream);saved.Normalize();Check(saved.RadialPages=="Companion,Notes","selected entries survive settings serialization");}
   pet.OpenPanel();var menu=pet.Menu;menu.UpdateLayout();var root=(FrameworkElement)menu.Content;
   var canvas=(Canvas)((Viewbox)root).Child;
   int count=0;foreach(var child in canvas.Children)if(child is RadialSlice)count++;
   Check(count==8,"exactly eight hit-tested sectors");
   for(int i=0;i<8;i++){
    var point=RadialMenuLayout.At(211,-90+i*45);
    var hit=VisualTreeHelper.HitTest(canvas,point);var slice=hit==null?null:hit.VisualHit as RadialSlice;
    Check(slice!=null,"sector "+(i+1)+" has a clickable center");
    var geometry=RadialMenuLayout.Slice(i);Check(!geometry.FillContains(new Point(0,0))&&!geometry.FillContains(new Point(320,320)),"sector excludes transparent corners and center");
   }
   var bitmap=new RenderTargetBitmap((int)menu.ActualWidth,(int)menu.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory("tests/popup-preview");using(var file=File.Create("tests/popup-preview/radial-default.png"))encoder.Save(file);
   Button removeCompanion=null;foreach(var child in canvas.Children){var b=child as Button;if(b!=null&&b.Tag is PanelTab&&(PanelTab)b.Tag==PanelTab.Companion)removeCompanion=b;}
   Check(removeCompanion!=null&&removeCompanion.Visibility==Visibility.Visible,"visible sector has a small removal control");
   removeCompanion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));menu.UpdateLayout();
   Check(menu.IsVisible&&!RadialMenuLayout.Visible(pet.State.RadialPages,PanelTab.Companion)&&removeCompanion.Visibility==Visibility.Collapsed,"minus immediately clears sector without opening a page");
   Check(PluginAccess.Page(pet.State,PanelTab.Companion),"removing sector leaves feature available");
   pet.State.RadialPages="";menu.RefreshEntries();RadialSlice blank=null;foreach(var child in canvas.Children)if(child is RadialSlice){blank=(RadialSlice)child;break;}
   blank.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!menu.IsVisible&&pet.Panel.IsVisible,"empty sector opens plugin management while hiding wheel");
   var panel=pet.Panel;Check((PanelTab)((TabItem)panel.Tabs.SelectedItem).Tag==PanelTab.Plugins,"plus opens plugin page even when every entry is hidden");
   foreach(var entry in RadialMenuLayout.Entries){pet.State.RadialPages=RadialMenuLayout.Toggle(pet.State.RadialPages,entry.Page,true);pet.OpenPanel(entry.Page);Check((PanelTab)((TabItem)panel.Tabs.SelectedItem).Tag==entry.Page,"route opens "+entry.Page);}
   pet.OpenPanel();Check(menu.IsVisible&&!panel.IsVisible,"return to wheel hides feature page without disposing it");pet.OpenPanel(PanelTab.Companion);Check(object.ReferenceEquals(panel,pet.Panel),"return to a feature reuses its input state");
   pet.ToggleMenu();Check(menu.IsVisible&&!panel.IsVisible,"shortcut action returns from feature page to wheel");pet.ToggleMenu();Check(!menu.IsVisible,"second shortcut action dismisses wheel");
   panel.Close();menu.Shutdown();pet.Menu=null;pet.State.RadialPages=RadialMenuLayout.DefaultPages;
  }
 }
}

