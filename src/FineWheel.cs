using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
internal sealed partial class PadHop {
 double pendingWheel;bool wheelScheduled;
 static double WheelPixels(int delta,int lines,double viewport){return delta/120.0*(lines<0?viewport:lines*16.0);}
 void InstallFineWheel(){var viewer=Get<ScrollViewer>("PageScroll");viewer.PreviewMouseWheel+=delegate(object sender,System.Windows.Input.MouseWheelEventArgs e){if(e.Handled)return;DependencyObject node=e.OriginalSource as DependencyObject;while(node!=null && !(node is ScrollViewer)){node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);}if(node!=viewer)return;e.Handled=true;pendingWheel+=WheelPixels(e.Delta,SystemParameters.WheelScrollLines,viewer.ViewportHeight);if(wheelScheduled)return;wheelScheduled=true;viewer.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(delegate{double amount=pendingWheel;pendingWheel=0;wheelScheduled=false;viewer.ScrollToVerticalOffset(Math.Max(0,Math.Min(viewer.ScrollableHeight,viewer.VerticalOffset-amount)));}));};}
 void TestScrollEditor(){
  var original=Clone(padBook);padTabs.SelectedIndex=0;LoadPad();
  scrollDirection.SelectedIndex=1;Set("reversal",1.2);ReadEditor();
  if(!CurrentPad.ReverseScroll || CurrentPad.ScrollReversalPercent!=1.2)throw new Exception("Scroll editor did not save draft");
  padBook=Clone(padBook);LoadPad();if(scrollDirection.SelectedIndex!=1)throw new Exception("Scroll direction lost on reload");
  actionBox.SelectedIndex=0;if(scrollDirectionHost.Visibility!=Visibility.Collapsed || parameterCells["reversal"].Visibility!=Visibility.Collapsed)throw new Exception("Scroll controls shown in mouse mode");
  padBook=original;LoadPad();
 }
 static void TestFineWheel(){double tiny=0;for(int i=0;i<120;i++)tiny+=WheelPixels(1,3,500);if(Math.Abs(tiny-WheelPixels(120,3,500))>.00001 || WheelPixels(-120,3,500)!=-48 || WheelPixels(120,0,500)!=0 || WheelPixels(120,-1,500)!=500)throw new Exception("Fine wheel magnitude or system settings lost");}
}
