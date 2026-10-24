// Generated from Services/TeamLogoService.cs; edit translator/adapters.
// Source SHA-256: 6b1a6d7e2692a6612b22d03e5eeefbe02242b03d544649c625b5da630dcc53b4
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Roche_Scoreboard.Models;

namespace Roche_Scoreboard.Services;

/// <summary>Downloads, caches and guarantees a transparent PNG team logo.</summary>
public static class TeamLogoService
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<string> GetTransparentLogoAsync(TeamCatalogEntry team, CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(
            NativeStoragePaths.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Roche_Scoreboard", "team_library", "logos");
        Directory.CreateDirectory(directory);

        // The cache key is versioned so older directory-flag previews are not
        // reused now that VAFA entries resolve the actual featured club crest.
        string outputPath = Path.Combine(directory, team.Id + "-crest-v2.png");
        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 128)
            return outputPath;

        string sourceUrl = Uri.TryCreate(team.LogoSource, UriKind.Absolute, out Uri? direct)
            && (direct.Scheme == Uri.UriSchemeHttps || direct.Scheme == Uri.UriSchemeHttp)
                ? direct.AbsoluteUri
                : await ResolveWikipediaImageAsync(team.LogoSource, cancellationToken).ConfigureAwait(false);
        byte[] data = await DownloadWithRetryAsync(sourceUrl, cancellationToken).ConfigureAwait(false);
        string extension = Path.GetExtension(new Uri(sourceUrl).AbsolutePath).ToLowerInvariant();
        if (extension is not (".svg" or ".png" or ".jpg" or ".jpeg" or ".webp")) extension = ".img";
        string sourcePath = Path.Combine(directory, team.Id + extension);
        await File.WriteAllBytesAsync(sourcePath, data, cancellationToken).ConfigureAwait(false);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConvertToTransparentPng(sourcePath, outputPath));
        return outputPath;
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RocheScoreboard/4.1 (team library logo cache)");
        return client;
    }

    private static async Task<byte[]> DownloadWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpResponseMessage response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            bool transient = response.StatusCode == HttpStatusCode.TooManyRequests
                             || (int)response.StatusCode >= 500;
            if (!transient || attempt >= 3)
            {
                response.EnsureSuccessStatusCode();
                throw new InvalidOperationException("The logo download failed.");
            }

            TimeSpan wait = response.Headers.RetryAfter?.Delta
                            ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            if (wait > TimeSpan.FromSeconds(12)) wait = TimeSpan.FromSeconds(12);
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> ResolveWikipediaImageAsync(string title, CancellationToken cancellationToken)
    {
        string url = "https://en.wikipedia.org/w/api.php?action=query&format=json&redirects=1" +
                     "&prop=pageimages&piprop=original&titles=" + Uri.EscapeDataString(title);
        using JsonDocument document = JsonDocument.Parse(
            await Client.GetStringAsync(url, cancellationToken).ConfigureAwait(false));

        JsonElement pages = document.RootElement.GetProperty("query").GetProperty("pages");
        foreach (JsonProperty page in pages.EnumerateObject())
        {
            if (page.Value.TryGetProperty("original", out JsonElement original)
                && original.TryGetProperty("source", out JsonElement source))
            {
                return source.GetString() ?? throw new InvalidOperationException("Logo source was empty.");
            }
        }

        // Club crests are often non-free images, so MediaWiki deliberately
        // omits them from pageimages/original. Inspect the page image list and
        // resolve the most likely logo file explicitly instead.
        string imageListUrl = "https://en.wikipedia.org/w/api.php?action=query&format=json&redirects=1" +
                              "&prop=images&imlimit=max&titles=" + Uri.EscapeDataString(title);
        using JsonDocument imageListDocument = JsonDocument.Parse(
            await Client.GetStringAsync(imageListUrl, cancellationToken).ConfigureAwait(false));
        JsonElement imagePages = imageListDocument.RootElement.GetProperty("query").GetProperty("pages");
        List<string> imageTitles = [];
        foreach (JsonProperty page in imagePages.EnumerateObject())
        {
            if (!page.Value.TryGetProperty("images", out JsonElement images)) continue;
            imageTitles.AddRange(images.EnumerateArray()
                .Select(image => image.GetProperty("title").GetString())
                .Where(static value => !string.IsNullOrWhiteSpace(value))!);
        }

        string? logoTitle = imageTitles
            .OrderByDescending(value => ScoreLogoCandidate(value!, title))
            .FirstOrDefault(value => ScoreLogoCandidate(value!, title) > 0);
        if (logoTitle is not null)
        {
            string infoUrl = "https://en.wikipedia.org/w/api.php?action=query&format=json" +
                             "&prop=imageinfo&iiprop=url&titles=" + Uri.EscapeDataString(logoTitle);
            using JsonDocument infoDocument = JsonDocument.Parse(
                await Client.GetStringAsync(infoUrl, cancellationToken).ConfigureAwait(false));
            JsonElement infoPages = infoDocument.RootElement.GetProperty("query").GetProperty("pages");
            foreach (JsonProperty page in infoPages.EnumerateObject())
            {
                if (page.Value.TryGetProperty("imageinfo", out JsonElement info)
                    && info.GetArrayLength() > 0
                    && info[0].TryGetProperty("url", out JsonElement resolvedUrl))
                {
                    return resolvedUrl.GetString() ?? throw new InvalidOperationException("Logo URL was empty.");
                }
            }
        }

        throw new InvalidOperationException($"No club logo image was returned for {title}.");
    }

    private static int ScoreLogoCandidate(string fileTitle, string pageTitle)
    {
        string value = fileTitle.ToLowerInvariant();
        if (value.Contains("wordmark") || value.Contains("afl logo") || value.Contains("aflw logo")) return -100;
        int score = 0;
        if (value.Contains("logo")) score += 12;
        if (value.Contains("crest") || value.Contains("emblem")) score += 8;
        foreach (string word in pageTitle.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= 4 && value.Contains(word)) score += 3;
        }
        if (value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) score += 2;
        return score;
    }

    private static void ConvertToTransparentPng(string sourcePath, string outputPath)
    {
        IImage? image = ImageLoadHelper.Load(sourcePath);
        if (image is null)
            throw new InvalidOperationException("The downloaded logo could not be decoded.");

        const int maxDimension = 1024;
        double ratio = Math.Min(1, maxDimension / Math.Max(image.Size.Width, image.Size.Height));
        int width = Math.Max(1, (int)Math.Round(image.Size.Width * ratio));
        int height = Math.Max(1, (int)Math.Round(image.Size.Height * ratio));

        int stride = width * 4;
        byte[] pixels = NativeRaster.RenderPixels(image, width, height);

        bool hasTransparency = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 250) { hasTransparency = true; break; }
        }

        if (!hasTransparency)
            RemoveBorderConnectedBackground(pixels, width, height, stride);

        NativeBitmap result = NativeBitmap.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        result.Save(output);
    }

    /// <summary>
    /// Removes only background-like pixels connected to an image edge. This
    /// protects white marks inside a crest while removing a flat JPEG/PNG mat.
    /// </summary>
    private static void RemoveBorderConnectedBackground(byte[] pixels, int width, int height, int stride)
    {
        (double b, double g, double r) background = AverageCorners(pixels, width, height, stride);
        bool[] visited = new bool[width * height];
        Queue<(int x, int y)> queue = new();
        for (int x = 0; x < width; x++) { queue.Enqueue((x, 0)); queue.Enqueue((x, height - 1)); }
        for (int y = 1; y < height - 1; y++) { queue.Enqueue((0, y)); queue.Enqueue((width - 1, y)); }

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            int visitIndex = y * width + x;
            if (visited[visitIndex]) continue;
            visited[visitIndex] = true;

            int p = y * stride + x * 4;
            double db = pixels[p] - background.b;
            double dg = pixels[p + 1] - background.g;
            double dr = pixels[p + 2] - background.r;
            double distance = Math.Sqrt(db * db + dg * dg + dr * dr);
            if (distance > 82) continue;

            pixels[p + 3] = distance <= 42 ? (byte)0 : (byte)Math.Clamp((distance - 42) / 40 * 255, 0, 255);
            if (x > 0) queue.Enqueue((x - 1, y));
            if (x + 1 < width) queue.Enqueue((x + 1, y));
            if (y > 0) queue.Enqueue((x, y - 1));
            if (y + 1 < height) queue.Enqueue((x, y + 1));
        }
    }

    private static (double b, double g, double r) AverageCorners(byte[] pixels, int width, int height, int stride)
    {
        int patch = Math.Max(1, Math.Min(width, height) / 24);
        long b = 0, g = 0, r = 0, count = 0;
        foreach ((int startX, int startY) in new[] { (0, 0), (width - patch, 0), (0, height - patch), (width - patch, height - patch) })
        {
            for (int y = startY; y < startY + patch; y++)
            for (int x = startX; x < startX + patch; x++)
            {
                int p = y * stride + x * 4;
                b += pixels[p]; g += pixels[p + 1]; r += pixels[p + 2]; count++;
            }
        }
        return ((double)b / count, (double)g / count, (double)r / count);
    }
}
