using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Roche_Scoreboard.Models;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Brushes = System.Windows.Media.Brushes;
using FlowDirection = System.Windows.FlowDirection;

namespace Roche_Scoreboard.Services;

internal static class CricketClubChecks
{
    internal static async Task<Dictionary<string, bool>> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, bool>();
        var cricket = TeamCatalogService.GetTeams(TeamLibrarySport.Cricket);
        var football = TeamCatalogService.GetTeams();
        checks["cricket-directory-has-all-21-premier-and-32-vsdca-clubs"] = cricket.Count == 53
            && cricket.Count(team => team.League == "Victorian Premier") == 21 && cricket.Count(team => team.League == "VSDCA") == 32;
        checks["club-directories-are-separated-by-sport-with-no-overlapping-ids"] = cricket.All(team => team.Sport == TeamLibrarySport.Cricket)
            && football.All(team => team.Sport == TeamLibrarySport.AustralianFootball) && !cricket.Select(team => team.Id).Intersect(football.Select(team => team.Id)).Any();
        checks["cricket-clubs-have-names-abbreviations-colours-and-official-provenance"] = cricket.All(team => team.TeamName.Length > 0
            && team.Abbreviation.Length is > 0 and <= 5 && team.PrimaryColor.Length == 7 && team.SecondaryColor.Length == 7
            && Uri.TryCreate(team.SourcePage, UriKind.Absolute, out var source) && source.Scheme == Uri.UriSchemeHttps
            && new[] { "www.playhq.com", "www.vicpremiercricket.com.au", "www.boxhillcricketclub.com" }.Contains(source.Host) && team.BundledLogo is not null);
        var artwork = new List<(TeamCatalogEntry Team, BitmapSource Original, BitmapSource Prepared)>();
        foreach (var team in cricket)
        {
            try
            {
                string path = await TeamLogoService.PrepareBundledLogoAsync(team, directory);
                using var preparedStream = File.OpenRead(path);
                var prepared = BitmapDecoder.Create(preparedStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; prepared.Freeze();
                var converted = new FormatConvertedBitmap(prepared, PixelFormats.Bgra32, null, 0);
                byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
                int visible = 0, clear = 0;
                for (int i = 3; i < pixels.Length; i += 4) { if (pixels[i] > 128) visible++; if (pixels[i] == 0) clear++; }
                int edgeClear = 0, edgeCount = 0;
                void Edge(int p) { edgeCount++; if (pixels[p + 3] < 16) edgeClear++; }
                int stride = converted.PixelWidth * 4;
                for (int x = 0; x < converted.PixelWidth; x++) { Edge(x * 4); Edge((converted.PixelHeight - 1) * stride + x * 4); }
                for (int y = 1; y + 1 < converted.PixelHeight; y++) { Edge(y * stride); Edge(y * stride + (converted.PixelWidth - 1) * 4); }
                checks["club-crest-transparent-and-not-blank-" + team.Id] = clear > 0 && visible > converted.PixelWidth * converted.PixelHeight / 500
                    && edgeClear > edgeCount * .5;
                if (team.Id == "cricket-vpc-footscray")
                {
                    int body = (int)(converted.PixelHeight * 310d / 1080) * stride + (int)(converted.PixelWidth * 650d / 1080) * 4;
                    int hindBody = (int)(converted.PixelHeight * 350d / 1080) * stride + (int)(converted.PixelWidth * 700d / 1080) * 4;
                    checks["footscray-white-bulldog-body-is-retained-with-transparent-outer-canvas"] = pixels[body + 3] == 255
                        && pixels[hindBody + 3] == 255 && pixels[body] > 240 && pixels[body + 1] > 240 && pixels[body + 2] > 240 && pixels[3] == 0;
                }
                using var originalStream = System.Windows.Application.GetResourceStream(new Uri("/" + team.BundledLogo, UriKind.Relative))!.Stream;
                var original = BitmapDecoder.Create(originalStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]; original.Freeze();
                artwork.Add((team, original, prepared));
            }
            catch (Exception error)
            {
                checks["club-crest-transparent-and-not-blank-" + team.Id] = false;
                File.AppendAllText(Path.Combine(directory, "errors.txt"), team.Id + ": " + error + Environment.NewLine);
            }
        }
        for (int page = 0; page * 18 < artwork.Count; page++)
        {
            var items = artwork.Skip(page * 18).Take(18).ToArray();
            var visual = new DrawingVisual(); int width = 700, height = items.Length * 106 + 45;
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x22)), null, new Rect(0, 0, width, height));
                Label(dc, "OFFICIAL ORIGINAL                  TRANSPARENT CACHE", 230, 10);
                for (int i = 0; i < items.Length; i++)
                {
                    double y = i * 106 + 40; var item = items[i];
                    Label(dc, item.Team.TeamName, 10, y + 26); Label(dc, item.Team.League, 10, y + 48);
                    DrawFit(dc, item.Original, 230, y, 160, 96); DrawFit(dc, item.Prepared, 450, y, 160, 96);
                }
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, $"club-crests-original-versus-prepared-{page + 1}.png")); encoder.Save(output);
        }
        return checks;
    }
    private static void Label(DrawingContext dc, string text, double x, double y)
        => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 13, Brushes.White, 1), new System.Windows.Point(x, y));
    private static void DrawFit(DrawingContext dc, BitmapSource image, double x, double y, double width, double height)
    {
        double scale = Math.Min(width / image.PixelWidth, height / image.PixelHeight);
        double w = image.PixelWidth * scale, h = image.PixelHeight * scale;
        dc.DrawImage(image, new Rect(x + (width - w) / 2, y + (height - h) / 2, w, h));
    }
}
