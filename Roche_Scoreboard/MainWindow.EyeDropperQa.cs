// Optional method for an existing Windows MainWindow QA partial.
// This snippet performs no desktop sampling, P/Invoke, overlay creation or file I/O.
// Hook it into an already authorized QA entry point; not executed by the author.
namespace Roche_Scoreboard;

public partial class MainWindow
{
    private static void VerifyEyeDropperColorRefDecoder(
        System.Collections.Generic.IDictionary<string, bool> checks)
    {
        void Expect(string name, uint colorRef, System.Windows.Media.Color? expected)
        {
            var actual = Services.EyeDropper.DecodeColorRef(colorRef);
            bool equal = actual == expected;
            checks[name] = equal;
            if (!equal)
                throw new System.InvalidOperationException(
                    $"{name}: COLORREF 0x{colorRef:X8}, expected {expected?.ToString() ?? "failure"}, got {actual?.ToString() ?? "failure"}.");
        }

        Expect("eyedropper-valid-black-is-a-colour", 0x00000000u,
            System.Windows.Media.Color.FromRgb(0, 0, 0));
        Expect("eyedropper-valid-white-is-a-colour", 0x00FFFFFFu,
            System.Windows.Media.Color.FromRgb(255, 255, 255));
        Expect("eyedropper-colorref-decodes-rgb-byte-order", 0x00332211u,
            System.Windows.Media.Color.FromRgb(0x11, 0x22, 0x33));
        Expect("eyedropper-clr-invalid-is-failure", 0xFFFFFFFFu, null);
        // Rejection must not poison a following valid white/black sample.
        Expect("eyedropper-white-after-failure-stays-valid", 0x00FFFFFFu,
            System.Windows.Media.Color.FromRgb(255, 255, 255));
        Expect("eyedropper-black-after-failure-stays-valid", 0x00000000u,
            System.Windows.Media.Color.FromRgb(0, 0, 0));
    }
}
