using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Point=System.Windows.Point;
using Color=System.Windows.Media.Color;
using Brush=System.Windows.Media.Brush;
using Brushes=System.Windows.Media.Brushes;
using Pen=System.Windows.Media.Pen;

namespace Roche_Scoreboard.Views;

/// <summary>Cricket broadcast graphics: native drawing, shared axes and no operator glass.</summary>
internal sealed class CricketChartVisual:FrameworkElement
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
    {try{return (Color)System.Windows.Media.ColorConverter.ConvertFromString(value);}catch{return Colors.LightSkyBlue;}}
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
    protected override void OnRender(DrawingContext d)
    {
        double width=ActualWidth,height=ActualHeight;
        if(width<100 || height<100)return;
        d.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        var face=new Typeface(CricketBroadcastStyle.Typeface,FontStyles.Normal,FontWeights.Bold,FontStretches.Normal);
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        void Text(string value,double size,double x,double y,Brush? ink=null,bool center=false,double maxWidth=double.PositiveInfinity)
        {
            var text=new FormattedText(value,CultureInfo.InvariantCulture,System.Windows.FlowDirection.LeftToRight,face,size,ink??Brushes.White,dpi);
            if(double.IsFinite(maxWidth)){text.MaxTextWidth=Math.Max(1,maxWidth);text.MaxLineCount=1;text.Trimming=TextTrimming.CharacterEllipsis;}
            d.DrawText(text,new Point(center?x-text.WidthIncludingTrailingWhitespace/2:x,y));
        }
        var colours=_series.Select(series=>Readable(Colour(series.Colour))).ToArray();
        for(int i=1;i<_series.Count;i++)
        {
            if(_series[i].Name==_series[0].Name || Distance(colours[0],colours[i])>=150)continue;
            var secondary=Readable(Colour(_series[i].Secondary));
            colours[i]=Distance(colours[0],secondary)>=150?secondary:new[]{"#FFDA7B","#7CE8FF","#DDAAFF"}.Select(value=>Readable(Colour(value))).OrderByDescending(value=>Distance(colours[0],value)).First();
        }
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
            if(similar && index==1)d.DrawLine(new Pen(Brushes.White,3){DashStyle=DashStyles.Dash},new Point(left+12,69),new Point(left+span-12,69));
            string name=string.IsNullOrWhiteSpace(series.Abbreviation)?series.Name:series.Abbreviation;
            string ordinal=series.TeamInnings switch{1=>"1ST",2=>"2ND",3=>"3RD",_=>series.TeamInnings+"TH"};
            Text(name.ToUpperInvariant()+" · "+ordinal,36,left+18,5,maxWidth:span-34);
            Text($"{series.TotalRuns} R · {series.LegalBalls/6}.{series.LegalBalls%6} OV",22,left+18,45,maxWidth:span-34);
            if(series.TeamInnings>1)d.DrawLine(new Pen(Brushes.White,3){DashStyle=DashStyles.Dash},new Point(left+12,73),new Point(left+span-12,73));
        }
        if(!_series.Any(series=>series.Progress.Count>1))
        {Text("NO DELIVERIES YET",42,width/2,height/2,center:true);d.Pop();return;}
        bool incomplete=_series.Any(series=>series.HistoryIncomplete);
        double legendBottom=incomplete?120:94,bottom=height-94;
        if(incomplete)Text("ADJUSTED SCORECARD · RECORDED BALL HISTORY ONLY",25,12,83,maxWidth:width-24);
        var plot=new Rect(86,legendBottom,Math.Max(1,width-112),Math.Max(1,bottom-legendBottom));
        int maxOver=Math.Max(1,_series.SelectMany(series=>series.Overs).Select(point=>point.Over).DefaultIfEmpty(1).Max());
        int pageSize=_series.Count>2?12:20;
        int pageCount=Math.Max(1,(maxOver+pageSize-1)/pageSize);
        FirstOver=(Math.Max(0,_page)%pageCount)*pageSize+1;LastOver=Math.Min(maxOver,FirstOver+pageSize-1);
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
        string overCaption=_manhattan?$"OVERS {FirstOver}–{LastOver}":"OVERS";
        if(_manhattan && _series.Any(series=>series.LegalBalls%6!=0))overCaption+=" · INCLUDES PARTIAL OVERS";
        Text(overCaption,28,plot.Left+plot.Width/2,height-39,center:true,maxWidth:plot.Width);
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
                    if(_series[index].TeamInnings>1 || similar && index==1)d.DrawLine(new Pen(Brushes.Black,Math.Min(2,barWidth*.2)),new Point(x+barWidth*.5,y),new Point(x+barWidth*.5,plot.Bottom));
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
                    foreach(var point in series.Progress.Skip(1))
                    {
                        var position=new Point(plot.Left+plot.Width*(point.LegalBalls/6d)/xMaximum,Y(point.Runs));
                        if(point.BreakBefore)path.BeginFigure(position,false,false);else path.LineTo(position,true,false);
                    }
                }
                geometry.Freeze();var pen=new Pen(ink,5){LineJoin=PenLineJoin.Round};if(series.TeamInnings>1 || similar && index==1)pen.DashStyle=DashStyles.Dash;
                d.DrawGeometry(null,new Pen(Brushes.Black,8),geometry);d.DrawGeometry(null,pen,geometry);
                foreach(var point in series.Progress.Where(point=>point.Wickets>0))
                    d.DrawEllipse(ink,new Pen(Brushes.White,2),new Point(plot.Left+plot.Width*(point.LegalBalls/6d)/xMaximum,Y(point.Runs)),5.5,5.5);
                foreach(var point in series.Progress.Where(point=>point.BreakBefore))
                    d.DrawEllipse(null,new Pen(ink,3),new Point(plot.Left+plot.Width*(point.LegalBalls/6d)/xMaximum,Y(point.Runs)),6,6);
            }
        }
        d.Pop();
    }
}
