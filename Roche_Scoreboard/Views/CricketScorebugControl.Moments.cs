using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Roche_Scoreboard.Models;
using Roche_Scoreboard.Services;
using Canvas = System.Windows.Controls.Canvas;
using Brushes = System.Windows.Media.Brushes;

namespace Roche_Scoreboard.Views;

public partial class CricketScorebugControl
{
    private void AnimateCricketMoment(CricketDeliveryType moment)
    {
        EventAccents.Children.Clear();
        EventWord.BeginAnimation(OpacityProperty, null);
        EventWord.Opacity = 0; // Retained as the semantic word; the visible letters form the same word.
        EventWord.RenderTransform = Transform.Identity;
        string word = moment switch { CricketDeliveryType.Four => "FOUR", CricketDeliveryType.Six => "SIX", _ => "WICKET" };
        double width = CricketDesignRoot.Width, height = CricketDesignRoot.Height;
        var stage = new Canvas { Tag = word.ToLowerInvariant() + "-type-scene", Width = width, Height = height, ClipToBounds = true };
        EventAccents.Children.Add(stage);
        if(moment==CricketDeliveryType.Six)
        {
            // One composed word travels through depth. Scaling the complete
            // word keeps its tracking intact throughout the camera impact.
            var depth=new ScaleTransform(6,6); stage.RenderTransform=depth; stage.RenderTransformOrigin=new System.Windows.Point(.5,.5);
            depth.BeginAnimation(ScaleTransform.ScaleXProperty,Impact(6,.90,1,1100,0));
            depth.BeginAnimation(ScaleTransform.ScaleYProperty,Impact(6,.90,1,1100,0));
        }
        var face = new Typeface(CricketBroadcastStyle.Typeface, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Measure(string value, double size) => new(value, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, face, size, Brushes.White, dpi);
        double font = Math.Min(height * .72, (width - 64) / Measure(word, 100).WidthIncludingTrailingWhitespace * 100);
        double wordWidth = Measure(word, font).WidthIncludingTrailingWhitespace;
        double left = (width - wordWidth) / 2;
        var shape = Measure(word, font);
        var ink = shape.BuildGeometry(new System.Windows.Point()).Bounds;
        double top = height / 2 - (ink.Top + ink.Height / 2);
        for (int i = 0; i < word.Length; i++)
        {
            string glyph = word[i].ToString();
            var letter = new TextBlock { Tag = "moment-letter", Text = glyph, FontFamily = CricketBroadcastStyle.Typeface, FontWeight = FontWeights.Bold, FontSize = font,
                Foreground = Brushes.White, Opacity = 0, RenderTransformOrigin = new System.Windows.Point(.5, .5) };
            Canvas.SetLeft(letter, left); Canvas.SetTop(letter, top); stage.Children.Add(letter);
            // Use shaped prefix advances so the final word has native tracking.
            left = (width - wordWidth) / 2 + Measure(word[..(i + 1)], font).WidthIncludingTrailingWhitespace;
            var move = new TranslateTransform(); var scale = new ScaleTransform(1, 1);
            var transform = new TransformGroup(); transform.Children.Add(scale); transform.Children.Add(move); letter.RenderTransform = transform;
            double delay = moment == CricketDeliveryType.Wicket ? i * 25 : moment == CricketDeliveryType.Four ? Math.Abs(i - 1.5) * 45 : 0;
            if (moment == CricketDeliveryType.Four)
            {
                // The two halves hit the same baseline from opposite sides.
                move.X = (i < 2 ? -1 : 1) * width;
                move.BeginAnimation(TranslateTransform.XProperty, Slam(move.X,0,900,delay));
                scale.ScaleX = 2.2; scale.ScaleY = .18;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, Impact(2.2, .94, 1, 950, delay));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, Impact(.18, 1.12, 1, 950, delay));
            }
            else if (moment == CricketDeliveryType.Six)
            {
                // An oversized word comes through the camera, then holds still.
                // The whole-word transform above owns this scene; no letters
                // move independently or temporarily acquire different tracking.
            }
            else
            {
                // A tight, heavy typographic stamp: the word lands in order,
                // compresses at impact, then locks into a readable hold.
                move.Y = -height*.8;
                scale.ScaleY = 2.4;scale.ScaleX = 1.45;
                move.BeginAnimation(TranslateTransform.YProperty,Slam(move.Y,0,820,delay));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,Impact(2.4,.88,1,880,delay));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,Impact(1.45,1.06,1,880,delay));
            }
            letter.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(65)) { BeginTime = TimeSpan.FromMilliseconds(delay), FillBehavior = FillBehavior.HoldEnd });
        }
    }

    private static DoubleAnimationUsingKeyFrames Slam(double from,double target,double milliseconds,double delay)
    {
        var animation=new DoubleAnimationUsingKeyFrames{Duration=TimeSpan.FromMilliseconds(milliseconds),BeginTime=TimeSpan.FromMilliseconds(delay),FillBehavior=FillBehavior.HoldEnd};
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from,KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(target-from*.015,KeyTime.FromPercent(.42),new CubicEase{EasingMode=EasingMode.EaseIn}));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(target,KeyTime.FromPercent(.72),new CubicEase{EasingMode=EasingMode.EaseOut}));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(target,KeyTime.FromPercent(1)));
        return animation;
    }

    private static DoubleAnimationUsingKeyFrames Impact(double from, double overshoot, double target, double milliseconds, double delay)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(milliseconds), BeginTime = TimeSpan.FromMilliseconds(delay), FillBehavior = FillBehavior.HoldEnd };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(from, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(overshoot, KeyTime.FromPercent(.42), new CubicEase { EasingMode = EasingMode.EaseIn }));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(target, KeyTime.FromPercent(.72), new CubicEase { EasingMode = EasingMode.EaseOut }));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(target,KeyTime.FromPercent(1)));
        return animation;
    }
}
