// Generated from Views/CricketChartVisual.cs; edit translator/adapters.
// Source SHA-256: c4169d6846000da841613fa1039a2fc0b786ac5e27f5cec0cfcba3000915eff6
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Globalization;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Point=Avalonia.Point;
using Color=Avalonia.Media.Color;
using Brush=Avalonia.Media.IBrush;
using Brushes=Avalonia.Media.Brushes;
using Pen=Avalonia.Media.Pen;

namespace Roche_Scoreboard.Views;

/// <summary>Cricket broadcast graphics: native drawing, shared axes and no operator glass.</summary>
internal sealed class CricketChartVisual:Control
{
    private readonly IReadOnlyList<CricketChartSeries> _series;
    private readonly bool _manhattan;
    private readonly int _page;
    internal int FirstOver{get;private set;}
    internal int LastOver{get;private set;}
    internal int SeriesCount=>_series.Count;
    internal CricketChartVisual(IReadOnlyList<CricketChartSeries> series,bool manhattan,int page)
    {_series=series;_manhattan=manhattan;_page=page;IsHitTestVisible=false;}
    private static Color Colour(string value)
    {try{return (Color)RocheScoreboard.Mac.Native.ColorConverter.ConvertFromString(value);}catch{return Colors.LightSkyBlue;}}
    private static double Distance(Color a,Color b)=>Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);
    private static Color Readable(Color colour)
    {var original=colour;
        for(int step=0;step<=10;step++)
        {
            if(ContrastHelper.ContrastRatio(colour,Color.FromRgb(11,17,32))>=4.5)return colour;
            double mix=(step+1)/10d;colour=Color.FromRgb((byte)(original.R+(255-original.R)*mix),(byte)(original.G+(255-original.G)*mix),(byte)(original.B+(255-original.B)*mix));
        }
        return Colors.White;}
    internal static double NiceMaximum(double value)
    {
        if(!double.IsFinite(value)||value<=0)return 10;
        double unit=Math.Pow(10,Math.Floor(Math.Log10(value))),normal=value/unit;
        return (normal<=1?1:normal<=2?2:normal<=5?5:10)*unit;
    }
    public override void Render(Avalonia.Media.DrawingContext nativeContext) { using var d = new NativeDrawingContext(nativeContext,Bounds.Size);
        double width=Bounds.Width,height=Bounds.Height;
        if(width<100 || height<100)return;
        d.PushClip(new RectangleGeometry(new Rect(Bounds.Size)));
        var face=new Typeface(CricketBroadcastStyle.Typeface,FontStyle.Normal,FontWeight.Bold,FontStretch.Normal);
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        void Text(string value,double size,double x,double y,Brush? ink=null,bool center=false,double maxWidth=double.PositiveInfinity)
        {
            var text=new NativeFormattedText(value,CultureInfo.InvariantCulture,Avalonia.Media.FlowDirection.LeftToRight,face,size,ink??Brushes.White,dpi);
            if(double.IsFinite(maxWidth)){text.MaxTextWidth=Math.Max(1,maxWidth);text.MaxLineCount=1;text.Trimming=TextTrimming.CharacterEllipsis;}
            d.DrawText(text,new Point(center?x-text.WidthIncludingTrailingWhitespace/2:x,y));
        }
        var colours=_series.Select(series=>Readable(Colour(series.Colour))).ToArray();
        bool similar=colours.Length==2 && Distance(colours[0],colours[1])<150;
        if(similar)
        {
            var secondary=Readable(Colour(_series[1].Secondary));
            if(Distance(colours[0],secondary)>=150){colours[1]=secondary;similar=false;}
        }
        for(int index=0;index<_series.Count;index++)
        {
            var series=_series[index];double left=index*width/Math.Max(1,_series.Count),span=width/Math.Max(1,_series.Count);
            var brush=new SolidColorBrush(colours[index]);
            d.DrawRectangle(CricketBroadcastStyle.Gradient(series.Colour,series.Secondary),null,new Rect(left,0,span-4,76));
            d.DrawRectangle(brush,null,new Rect(left,0,7,76));
            if(similar && index==1)d.DrawLine(new Pen(Brushes.White,3){DashStyle=DashStyle.Dash},new Point(left+12,69),new Point(left+span-12,69));
            string name=string.IsNullOrWhiteSpace(series.Abbreviation)?(series.Name ?? string.Empty):series.Abbreviation;
            string ordinal=series.Innings switch{1=>"1ST",2=>"2ND",3=>"3RD",_=>series.Innings+"TH"};
            Text(name.ToUpperInvariant()+" · "+ordinal,42,left+18,11,maxWidth:span-34);
        }
        if(!_series.Any(series=>series.Progress.Count>1))
        {Text("NO DELIVERIES YET",42,width/2,height/2,center:true);d.Pop();return;}
        double legendBottom=94,bottom=height-94;
        var plot=new Rect(86,legendBottom,Math.Max(1,width-112),Math.Max(1,bottom-legendBottom));
        int maxOver=Math.Max(1,_series.SelectMany(series=>series.Overs).Select(point=>point.Over).DefaultIfEmpty(1).Max());
        int pageCount=Math.Max(1,(maxOver+19)/20);
        FirstOver=(_page%pageCount)*20+1;LastOver=Math.Min(maxOver,FirstOver+19);
        double xMaximum=Math.Max(1,Math.Ceiling(_series.SelectMany(series=>series.Progress).Select(point=>point.LegalBalls/6d).DefaultIfEmpty(0).Max()));
        double maxValue=_manhattan?_series.SelectMany(series=>series.Overs).Where(point=>point.Over>=FirstOver && point.Over<=LastOver).Select(point=>(double)point.Runs).DefaultIfEmpty(0).Max():_series.SelectMany(series=>series.Progress).Select(point=>(double)point.Runs).DefaultIfEmpty(0).Max();
        double ceiling=NiceMaximum(Math.Max(1,maxValue)*1.12);
        double Y(double value)=>plot.Bottom-plot.Height*Math.Clamp(value/ceiling,0,1);
        var guide=new Pen(new SolidColorBrush(Color.FromRgb(70,80,96)),1);
        foreach(double tick in new[]{0,ceiling/2,ceiling})
        {
            double y=Y(tick);d.DrawLine(guide,new Point(plot.Left,y),new Point(plot.Right,y));
            Text(tick>=1000?(tick/1000).ToString("0.#",CultureInfo.InvariantCulture)+"k":tick.ToString("0.#",CultureInfo.InvariantCulture),36,0,y-23);
        }
        Text(_manhattan?$"OVERS {FirstOver}–{LastOver}":"OVERS",32,plot.Left+plot.Width/2,height-39,center:true);
        if(_manhattan)
        {
            int count=LastOver-FirstOver+1;double slot=plot.Width/count;
            for(int over=FirstOver;over<=LastOver;over++)
            {
                double center=plot.Left+(over-FirstOver+.5)*slot;
                if(over==FirstOver||over==LastOver||over%5==0)Text(over.ToString(),34,center,plot.Bottom+4,center:true);
                for(int index=0;index<_series.Count;index++)
                {
                    var point=_series[index].Overs.FirstOrDefault(point=>point.Over==over);if(point is null)continue;
                    double barWidth=slot*.78/Math.Max(1,_series.Count),x=center-slot*.39+index*barWidth;
                    double y=Y(point.Runs);var ink=new SolidColorBrush(colours[index]);
                    d.DrawRectangle(ink,null,new Rect(x,y,Math.Max(.8,barWidth-1),Math.Max(1,plot.Bottom-y)));
                    if(similar && index==1)d.DrawLine(new Pen(Brushes.Black,Math.Min(2,barWidth*.2)),new Point(x+barWidth*.5,y),new Point(x+barWidth*.5,plot.Bottom));
                    for(int wicket=0;wicket<point.Wickets;wicket++)
                        d.DrawEllipse(Brushes.White,new Pen(Brushes.Black,1),new Point(x+barWidth/2,Math.Max(plot.Top+4,y-6-wicket*9)),3.5,3.5);
                }
            }
        }
        else
        {
            double step=Math.Max(1,Math.Ceiling(xMaximum/4));
            for(double tick=0;tick<=xMaximum;tick+=step)
                Text(tick.ToString("0"),34,plot.Left+plot.Width*tick/xMaximum,plot.Bottom+4,center:true);
            for(int index=0;index<_series.Count;index++)
            {
                var series=_series[index];var ink=new SolidColorBrush(colours[index]);var geometry=new StreamGeometry();
                using(var path=geometry.Open())
                {
                    path.BeginFigure(new Point(plot.Left,plot.Bottom),false,false);
                    foreach(var point in series.Progress.Skip(1))path.LineTo(new Point(plot.Left+plot.Width*(point.LegalBalls/6d)/xMaximum,Y(point.Runs)),true,false);
                }
                geometry.Freeze();var pen=new Pen(ink,5){LineJoin=PenLineJoin.Round};if(similar && index==1)pen.DashStyle=DashStyle.Dash;
                d.DrawGeometry(null,new Pen(Brushes.Black,8),geometry);d.DrawGeometry(null,pen,geometry);
                foreach(var point in series.Progress.Where(point=>point.Wickets>0))
                    d.DrawEllipse(ink,new Pen(Brushes.White,2),new Point(plot.Left+plot.Width*(point.LegalBalls/6d)/xMaximum,Y(point.Runs)),5.5,5.5);
            }
        }
        d.Pop();
    }
}
