using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Roche_Scoreboard.Services;

internal sealed record CommandSearchCandidate(string Id, string Label, string Aliases, int Context = 0, bool Available = true);
internal sealed record CommandSearchHit(string Id, int Score, bool Approximate);

/// <summary>Bounded, deterministic local suggestions. Ranking never executes an action.</summary>
internal static class OperatorCommandSearch
{
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    { "please", "open", "show", "go", "to", "the", "my", "can", "you", "i", "want", "take", "me", "bring", "up", "turn", "on", "off", "down" };
    private static string Canonical(string word) => word switch
    {
        "volume" or "sound" or "mute" or "unmute" => "audio",
        "bowling" or "bowlers" => "bowler",
        "swap" or "choose" or "select" => "change",
        "continue" or "restore" or "recover" or "recovery" => "resume",
        "preferences" or "options" or "configuration" => "settings",
        "display" => "output",
        "footy" or "football" => "afl",
        "graphs" or "charts" or "graph" => "chart",
        _ => word
    };
    private static string[] Tokens(string value, bool query)
    {
        var text = new StringBuilder();
        foreach (char ch in value.Take(query ? 160 : 700)) text.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        return text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !query || !Noise.Contains(w))
            .SelectMany(w => query ? new[] { Canonical(w) } : new[] { w, Canonical(w) }).Distinct(StringComparer.Ordinal).Take(query ? 12 : 100).ToArray();
    }

    internal static IReadOnlyList<CommandSearchHit> Rank(string query, IEnumerable<CommandSearchCandidate> candidates)
    {
        var words = Tokens(query, true);
        var hits = new List<CommandSearchHit>();
        foreach (var candidate in candidates)
        {
            var label = Tokens(candidate.Label, false); var aliases = Tokens(candidate.Aliases, false);
            int score = Math.Clamp(candidate.Context, 0, 30) + (candidate.Available ? 12 : 0); bool approximate = false, matched = true;
            foreach (string word in words)
            {
                int best = 0;
                foreach (var (tokens, inLabel) in new[] { (label, true), (aliases, false) })
                foreach (string token in tokens)
                {
                    int quality = word == token ? (inLabel ? 110 : 85)
                        : token.StartsWith(word, StringComparison.Ordinal) ? (inLabel ? 90 : 65) : 0;
                    if (quality == 0 && word.Length >= 3 && token.Length <= 56)
                    {
                        int limit = word.Length >= 6 ? 2 : 1;
                        if (Math.Abs(word.Length - token.Length) <= limit)
                        { int distance = Distance(word, token); if (distance <= limit) quality = distance == 1 ? 42 : 28; }
                    }
                    best = Math.Max(best, quality);
                }
                if (best == 0) { matched = false; break; }
                score += best; approximate |= best < 60;
            }
            if (!matched) continue;
            if (words.Length > 0 && words.SequenceEqual(label)) score += 350;
            hits.Add(new(candidate.Id, score, approximate));
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Id, StringComparer.Ordinal).ToList();
    }

    // Optimal string alignment includes adjacent transpositions ("bolwer").
    private static int Distance(string left, string right)
    {
        var cost = new int[left.Length + 1, right.Length + 1];
        for (int i = 0; i <= left.Length; i++) cost[i, 0] = i;
        for (int j = 0; j <= right.Length; j++) cost[0, j] = j;
        for (int i = 1; i <= left.Length; i++) for (int j = 1; j <= right.Length; j++)
        {
            cost[i, j] = Math.Min(Math.Min(cost[i - 1, j] + 1, cost[i, j - 1] + 1), cost[i - 1, j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            if (i > 1 && j > 1 && left[i - 1] == right[j - 2] && left[i - 2] == right[j - 1])
                cost[i, j] = Math.Min(cost[i, j], cost[i - 2, j - 2] + 1);
        }
        return cost[left.Length, right.Length];
    }
}
