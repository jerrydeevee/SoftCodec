using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Brush=System.Windows.Media.Brush;
using Color=System.Windows.Media.Color;
using Point=System.Windows.Point;
namespace SoftCodec.App;
/// <summary>Two independent RMS meters. Segment geometry scales with the front panel.</summary>
public sealed class StereoMeter : FrameworkElement
{
 public static readonly DependencyProperty LeftDbProperty=DependencyProperty.Register(nameof(LeftDb),typeof(double),typeof(StereoMeter),new FrameworkPropertyMetadata(-96d,FrameworkPropertyMetadataOptions.AffectsRender));
 public static readonly DependencyProperty RightDbProperty=DependencyProperty.Register(nameof(RightDb),typeof(double),typeof(StereoMeter),new FrameworkPropertyMetadata(-96d,FrameworkPropertyMetadataOptions.AffectsRender));
 public double LeftDb{get=>(double)GetValue(LeftDbProperty);set=>SetValue(LeftDbProperty,value);}
 public double RightDb{get=>(double)GetValue(RightDbProperty);set=>SetValue(RightDbProperty,value);}
 static readonly Brush Off=new SolidColorBrush(Color.FromRgb(26,36,29));
 static readonly Brush Green=new SolidColorBrush(Color.FromRgb(98,217,105));
 static readonly Brush Amber=new SolidColorBrush(Color.FromRgb(239,188,65));
 static readonly Brush Red=new SolidColorBrush(Color.FromRgb(245,86,74));
 static readonly Brush Label=new SolidColorBrush(Color.FromRgb(147,159,151));
 protected override void OnRender(DrawingContext dc)
 {
  base.OnRender(dc);double width=Math.Max(1,ActualWidth-34);double barHeight=14;double top=25;const int segments=40;double cell=width/segments;
  for(int row=0;row<2;row++){
   double value=row==0?LeftDb:RightDb;Text(dc,row==0?"L":"R",0,top+row*25-1,Label,12);
   for(int i=0;i<segments;i++){double threshold=-60+(i+1)*60d/segments;Brush brush=value>=threshold?(threshold>-6?Red:threshold>-18?Amber:Green):Off;dc.DrawRoundedRectangle(brush,null,new Rect(24+i*cell,top+row*25,Math.Max(1,cell-2),barHeight),1,1);}
  }
  foreach(int tick in new[]{-60,-48,-36,-24,-12,0})Text(dc,tick.ToString(CultureInfo.InvariantCulture),24+(tick+60)*width/60-7,2,Label,10);
  Text(dc,"dBFS · RMS",24,77,Label,10);
 }
 void Text(DrawingContext dc,string text,double x,double y,Brush color,double size)=>dc.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,System.Windows.FlowDirection.LeftToRight,new Typeface("Consolas"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
}
