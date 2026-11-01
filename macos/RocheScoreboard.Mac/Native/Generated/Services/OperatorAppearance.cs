// Generated from Services/OperatorAppearance.cs; edit translator/adapters.
// Source SHA-256: aa94194cadd54863e6a9f0f1902e24b840a83d66d8193ff2ae608a6820aef0b8
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.IO;

namespace Roche_Scoreboard.Services;

internal enum OperatorGlassIntensity { Soft, Balanced, Vivid }
internal enum OperatorBackdropStyle { Black, Colour, Neutral }
internal enum OperatorMotionStyle { Calm, Expressive }

/// <summary>Operator-only material/motion choices; independent of public display pacing.</summary>
internal static class OperatorAppearance
{
    private sealed class Preferences
    {
        public Preferences() { }
        public bool GlassEnabled { get; set; } = true;
        public bool ReduceMotion { get; set; }
        public OperatorMotionStyle MotionStyle { get; set; } = OperatorMotionStyle.Expressive;
        public OperatorGlassIntensity GlassIntensity { get; set; } = OperatorGlassIntensity.Vivid;
        public OperatorBackdropStyle Backdrop { get; set; } = OperatorBackdropStyle.Colour;
    }
    private static readonly Preferences Current = Load();
    internal static bool GlassEnabled => Current.GlassEnabled;
    internal static bool ReduceMotion => Current.ReduceMotion;
    internal static OperatorMotionStyle MotionStyle => Current.MotionStyle;
    internal static OperatorGlassIntensity GlassIntensity => Current.GlassIntensity;
    internal static OperatorBackdropStyle Backdrop => Current.Backdrop;
    internal static event Action? Changed;
    internal static string StoragePath => Path.Combine(SelfTestStorage.DocumentsDirectory,"operator_appearance.json");
    private static Preferences Load()
    {
        try
        {
            var value=AtomicFile.TryReadJson<Preferences>(StoragePath) ?? new();
            if(!Enum.IsDefined(value.GlassIntensity))value.GlassIntensity=OperatorGlassIntensity.Vivid;
            if(!Enum.IsDefined(value.Backdrop))value.Backdrop=OperatorBackdropStyle.Colour;
            if(!Enum.IsDefined(value.MotionStyle))value.MotionStyle=OperatorMotionStyle.Expressive;
            return value;
        }
        catch { return new(); }
    }
    internal static void Configure(bool glassEnabled,bool reduceMotion)
    {
        if(Current.GlassEnabled==glassEnabled && Current.ReduceMotion==reduceMotion)return;
        Current.GlassEnabled=glassEnabled;Current.ReduceMotion=reduceMotion;
        Save();
    }
    internal static void ConfigureMaterial(OperatorGlassIntensity intensity,OperatorBackdropStyle backdrop)
    {
        if(!Enum.IsDefined(intensity) || !Enum.IsDefined(backdrop))return;
        if(Current.GlassIntensity==intensity && Current.Backdrop==backdrop)return;
        Current.GlassIntensity=intensity;Current.Backdrop=backdrop;Save();
    }
    internal static void ConfigureMotionStyle(OperatorMotionStyle style)
    {
        if(!Enum.IsDefined(style) || Current.MotionStyle==style)return;
        Current.MotionStyle=style;Save();
    }
    private static void Save()
    {
        try { AtomicFile.WriteAllText(StoragePath,System.Text.Json.JsonSerializer.Serialize(Current)); } catch(IOException) { } catch(UnauthorizedAccessException) { }
        Changed?.Invoke();
    }
}
