using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Roche_Scoreboard.Services;
using RocheScoreboard.Mac.Native;
using Xunit;

namespace RocheScoreboard.Mac.Tests;

[CollectionDefinition("Headless system motion preference", DisableParallelization = true)]
public sealed class HeadlessSystemMotionPreferenceCollection { }

[Collection("Headless system motion preference")]
public sealed class HeadlessSystemMotionPreferenceTests
{
    private sealed class Preferences : IDisposable
    {
        private readonly bool? system = NativeSystemPreferences.ClientAreaAnimationForTests;
        private readonly bool? animation = OperatorMotion.AnimationOverride;
        private readonly bool glass = OperatorAppearance.GlassEnabled;
        private readonly bool reduced = OperatorAppearance.ReduceMotion;
        private readonly byte[]? bytes = File.Exists(OperatorAppearance.StoragePath)
            ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;

        internal Preferences()
        {
            OperatorMotion.AnimationOverride = null;
            OperatorAppearance.Configure(true, false);
        }

        public void Dispose()
        {
            NativeSystemPreferences.ClientAreaAnimationForTests = system;
            OperatorMotion.AnimationOverride = animation;
            OperatorAppearance.Configure(glass, reduced);
            if (bytes is null) File.Delete(OperatorAppearance.StoragePath);
            else File.WriteAllBytes(OperatorAppearance.StoragePath, bytes);
        }
    }

    [AvaloniaTheory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void BothSystemAndAppReductionRemainAuthoritativeWithoutWritingSystemPreferences(
        bool systemAllowsMotion, bool appReducesMotion, bool expectedMotion)
    {
        using var preferences = new Preferences();
        OperatorAppearance.Configure(true, appReducesMotion);
        byte[]? saved = File.Exists(OperatorAppearance.StoragePath)
            ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null;

        NativeSystemPreferences.ClientAreaAnimationForTests = systemAllowsMotion;

        Assert.Equal(systemAllowsMotion, NativeSystemPreferences.ClientAreaAnimation);
        Assert.Equal(appReducesMotion, OperatorAppearance.ReduceMotion);
        Assert.Null(OperatorMotion.AnimationOverride);
        Assert.Equal(expectedMotion, OperatorMotion.Enabled);
        Assert.Equal(saved, File.Exists(OperatorAppearance.StoragePath)
            ? File.ReadAllBytes(OperatorAppearance.StoragePath) : null);
    }

    [AvaloniaFact]
    public async Task SystemPreferenceChangesStillNotifyOwnersAndCannotBypassAppReduction()
    {
        using var preferences = new Preferences();
        NativeSystemPreferences.ClientAreaAnimationForTests = true;
        var observed = new List<bool>();
        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(NativeSystemPreferences.ClientAreaAnimation))
                observed.Add(NativeSystemPreferences.ClientAreaAnimation);
        }
        async Task UntilCount(int count)
        {
            var watch = Stopwatch.StartNew();
            while (observed.Count < count && watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(count, observed.Count);
        }

        NativeSystemPreferences.StaticPropertyChanged += Changed;
        try
        {
            Assert.True(OperatorMotion.Enabled);
            NativeSystemPreferences.ClientAreaAnimationForTests = false;
            await UntilCount(1);
            Assert.Equal(new[] { false }, observed);
            Assert.False(OperatorMotion.Enabled);

            OperatorAppearance.Configure(true, true);
            NativeSystemPreferences.ClientAreaAnimationForTests = true;
            await UntilCount(2);
            Assert.Equal(new[] { false, true }, observed);
            Assert.False(OperatorMotion.Enabled);

            OperatorAppearance.Configure(true, false);
            Assert.True(OperatorMotion.Enabled);
            Assert.Null(OperatorMotion.AnimationOverride);
        }
        finally { NativeSystemPreferences.StaticPropertyChanged -= Changed; }
    }
}
