using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using Point = System.Windows.Point;

namespace Roche_Scoreboard.Views;

/// <summary>Tabular digit reel: only changed digits move; separators and unchanged digits stay fixed.</summary>
public sealed class RollingScoreVisual : FrameworkElement
{
    private readonly Stopwatch _clock = new();
    private TextBlock? _text;
    private string _from = "", _target = "";
    private double _progress, _duration;
    private bool _running;
    private int _direction=1;
    internal bool IsDecrementing => _direction<0;
    private readonly Dictionary<char,FormattedText> _glyphs=new();
    private Typeface? _cachedTypeface;
    private FormattedText? _shaped;
    private string? _shapedTarget;
    private double[] _edges=[];
    private double _cachedSize,_cachedDpi;
    internal double AnimatedAdvance { get; private set; }
    internal double RestingAdvance { get; private set; }
    private System.Windows.Media.Brush? _cachedBrush;
    public int MovingDigitCount => _from.Zip(_target).Count(pair=>pair.First!=pair.Second);
    public RollingScoreVisual() { IsHitTestVisible=false; ClipToBounds=true; Unloaded+=(_,_)=>Stop(); }
    public void Roll(TextBlock text,int from,int target)
    {
        if (target == from) { Stop(); text.Text=target.ToString(CultureInfo.InvariantCulture); return; }
        RollText(text,from.ToString(CultureInfo.InvariantCulture),target.ToString(CultureInfo.InvariantCulture));
    }
    public void RollText(TextBlock text,string previous,string target)
    {
        if (previous==target) return;
        Stop(); _text=text; text.Text=target;
        if (!IsVisible || !SystemParameters.ClientAreaAnimation || !target.Any(char.IsDigit) || previous.Length==0) return;
        int length=Math.Max(previous.Length,target.Length);
        _direction=CompareNumeric(previous,target)>0 ? -1 : 1;
        _from=previous.PadLeft(length); _target=target.PadLeft(length);
        _duration=760; _progress=0; _running=true; text.BeginAnimation(OpacityProperty,null); text.Opacity=0;
        _clock.Restart(); CompositionTarget.Rendering+=Frame; InvalidateVisual();
    }
    private static int CompareNumeric(string previous,string target)
    {
        if(decimal.TryParse(previous,NumberStyles.Number,CultureInfo.InvariantCulture,out decimal a)
            && decimal.TryParse(target,NumberStyles.Number,CultureInfo.InvariantCulture,out decimal b)) return a.CompareTo(b);
        var before=System.Text.RegularExpressions.Regex.Matches(previous,@"\d+");
        var after=System.Text.RegularExpressions.Regex.Matches(target,@"\d+");
        for(int i=0;i<Math.Min(before.Count,after.Count);i++)
            if(long.TryParse(before[i].Value,out long x) && long.TryParse(after[i].Value,out long y) && x!=y) return x.CompareTo(y);
        return before.Count.CompareTo(after.Count);
    }
    private void Frame(object? sender,EventArgs args)
    {
        double t=Math.Clamp(_clock.Elapsed.TotalMilliseconds/_duration,0,1);
        _progress=t*t*t*(10+t*(-15+6*t));
        if(t>=1) { Stop(); return; }
        InvalidateVisual();
    }
    public void Stop()
    {
        CompositionTarget.Rendering-=Frame; _clock.Stop(); _running=false;
        if (_text!=null) _text.Opacity=1;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if(!_running || _text is null || ActualWidth<=0 || ActualHeight<=0) return;
        var typeface=new Typeface(_text.FontFamily,_text.FontStyle,_text.FontWeight,_text.FontStretch);
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if(!Equals(_cachedTypeface,typeface) || _cachedSize!=_text.FontSize || _cachedDpi!=dpi || !ReferenceEquals(_cachedBrush,_text.Foreground))
        {
            _glyphs.Clear(); _shaped=null; _cachedTypeface=typeface; _cachedSize=_text.FontSize; _cachedDpi=dpi; _cachedBrush=_text.Foreground;
        }
        FormattedText Measure(char c)
        {
            if(!_glyphs.TryGetValue(c,out var glyph))
                _glyphs[c]=glyph=new FormattedText(c.ToString(),CultureInfo.InvariantCulture,System.Windows.FlowDirection.LeftToRight,
                    typeface,_text.FontSize,_text.Foreground,dpi);
            return glyph;
        }
        // Use the target TextBlock's actual origin and shaped advances, never a
        // separate widest-digit grid. That grid caused the tracking to expand
        // during a roll, then snap back when the native text was revealed.
        if(_shaped is null || _shapedTarget!=_target)
        {
            _shapedTarget=_target;
            _shaped=new FormattedText(_target,CultureInfo.InvariantCulture,System.Windows.FlowDirection.LeftToRight,typeface,_text.FontSize,_text.Foreground,dpi);
            _edges=new double[_target.Length+1];
            for(int i=0;i<_target.Length;i++)
            {
                var geometry=_shaped.BuildHighlightGeometry(new Point(0,0),i,1);
                _edges[i]=geometry?.Bounds.Left ?? (i==0 ? 0 : _edges[i-1]);
            }
            _edges[^1]=_shaped.WidthIncludingTrailingWhitespace;
        }
        var shaped=_shaped;
        var origin=_text.TranslatePoint(new Point(0,0),this);
        var edges=_edges;
        // A decrement can lose a leading digit. Its padded reel cell belongs to
        // the departing number, not the resting target's tracking/origin.
        int prefix=Math.Max(0,_target.Length-_text.Text.Length);
        double prefixAdvance=prefix>0 ? edges[prefix] : 0;
        origin.X-=prefixAdvance;
        RestingAdvance=_text.ActualWidth; AnimatedAdvance=shaped.WidthIncludingTrailingWhitespace-prefixAdvance;
        double x=origin.X;
        dc.PushClip(new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight)));
        for(int i=0;i<_target.Length;i++)
        {
            char from=_from[i],target=_target[i]; double width=Math.Max(0,edges[i+1]-edges[i]); x=origin.X+edges[i];
            if(from==target)
            {
                dc.PushClip(new RectangleGeometry(new Rect(x,0,Math.Max(.1,width),ActualHeight)));
                dc.DrawText(shaped,origin); dc.Pop();
            }
            else if(char.IsDigit(from)&&char.IsDigit(target))
            {
                int steps=_direction>0 ? (target-from+10)%10 : (from-target+10)%10; double position=steps*_progress;
                int whole=(int)Math.Floor(position); double fraction=position-whole;
                int digit=(from-'0'+_direction*whole+20)%10;
                Draw((char)('0'+digit),-_direction*fraction*ActualHeight);
                Draw((char)('0'+(digit+_direction+10)%10),_direction*(1-fraction)*ActualHeight);
            }
            else { Draw(from,-_direction*_progress*ActualHeight); Draw(target,_direction*(1-_progress)*ActualHeight); }
            void Draw(char c,double y)
            {
                if(c==' ') return;
                var glyph=Measure(c); dc.DrawText(glyph,new Point(x,origin.Y+y));
            }
        }
        dc.Pop();
    }
}
