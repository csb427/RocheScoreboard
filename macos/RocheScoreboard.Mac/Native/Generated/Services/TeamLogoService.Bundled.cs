// Generated from Services/TeamLogoService.Bundled.cs; edit translator/adapters.
// Source SHA-256: 1005067c572c590b66fa1dfbba45749e672ac1ce60f67873a64fcfa71ca324ea
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.IO;
using Avalonia.Media.Imaging;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

public static partial class TeamLogoService
{
    private static readonly SemaphoreSlim BundledPreparation = new(1, 1);
    internal static async Task<string> PrepareBundledLogoAsync(TeamCatalogEntry team, string? cacheDirectory, CancellationToken cancellationToken = default)
    {
        string directory = cacheDirectory ?? Path.Combine(NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Roche_Scoreboard", "team_library", "logos");
        Directory.CreateDirectory(directory);
        string output = Path.Combine(directory, team.Id + "-crest-v5.png");
        await BundledPreparation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(output) && new FileInfo(output).Length > 128) return output;
            byte[] bytes = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var resource = NativeAssets.Open(new Uri("/" + team.BundledLogo, UriKind.Relative));
                using (resource)
                using (var source = new MemoryStream()) { resource.CopyTo(source); return source.ToArray(); }
            });
            await Task.Run(() => PrepareRaster(bytes, output, team), cancellationToken).ConfigureAwait(false);
            return output;
        }
        finally { BundledPreparation.Release(); }
    }
    private static void PrepareRaster(byte[] bytes, string output, TeamCatalogEntry team)
    {
        using var input = new MemoryStream(bytes);
        using var decoded = new NativeBitmap { StreamSource = input }; decoded.BeginInit(); decoded.EndInit();
        if (Math.Max(decoded.PixelWidth, decoded.PixelHeight)>1024) { decoded.DecodePixelWidth=decoded.PixelWidth>=decoded.PixelHeight?1024:0; decoded.DecodePixelHeight=decoded.PixelHeight>decoded.PixelWidth?1024:0; input.Position=0; decoded.EndInit(); }
        var converted = decoded;
        int width = converted.PixelWidth, height = converted.PixelHeight, stride = width * 4;
        byte[] pixels = new byte[stride * height]; converted.CopyPixels(pixels, stride, 0);
        // Some official PNGs have a flattened matte with alpha 230 everywhere.
        // Existing real transparent silhouettes are retained exactly; otherwise
        // remove only matte-coloured pixels connected to the image edge.
        int clearEdgePixels = 0, edgePixels = 0;
        void ObserveEdge(int p) { edgePixels++; if (pixels[p + 3] < 16) clearEdgePixels++; }
        for (int x = 0; x < width; x++) { ObserveEdge(x * 4); ObserveEdge((height - 1) * stride + x * 4); }
        for (int y = 1; y + 1 < height; y++) { ObserveEdge(y * stride); ObserveEdge(y * stride + (width - 1) * 4); }
        // A few transparent corner pixels do not prove the matte is gone.
        // Genuine cutouts have a substantially clear outer perimeter.
        bool clearEdge = clearEdgePixels > edgePixels * .5;
        if (!clearEdge)
        {
            // This official Footscray JPEG has a deliberate opening between the
            // red neck and back strokes. White body pixels otherwise connect to
            // the outer matte. Block only that tiny flood passage, retaining the
            // original RGB throughout. The guard is tied to the exact immutable
            // source bytes, so another version of the crest is never masked by
            // assumptions belonging to this one.
            bool footscraySource = team.Id == "cricket-vpc-footscray"
                && team.BundledLogo == "Resources/ClubLogos/cricket-vpc-footscray-directory.jpg"
                && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) == "618F99FCFD565B1C01E81015AD2B188CA9BCFC098B77C1E677990F78ED240922";
            Func<int, int, bool>? guard = footscraySource
                ? (x, y) => x >= Math.Floor(width * 519d / 1080) && x <= Math.Ceiling(width * 546d / 1080)
                    && y >= Math.Floor(height * 207d / 1080) && y <= Math.Ceiling(height * 215d / 1080)
                : null;
            RemoveBorderConnectedBackground(pixels, width, height, stride, guard);
        }
        int visible = 0, transparent = 0;
        for (int i = 3; i < pixels.Length; i += 4) { if (pixels[i] > 128) visible++; if (pixels[i] == 0) transparent++; }
        if (visible < width * height / 500 || transparent == 0)
            throw new IOException("This crest could not be safely separated from its background. Choose a custom transparent logo instead.");
        var result = NativeBitmap.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        
        string temporary = output + ".tmp";
        try { using (var file = File.Create(temporary)) result.Save(file); File.Move(temporary, output, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
