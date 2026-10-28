// Generated from Services/Cloud/JoinCodeGenerator.cs; edit translator/adapters.
// Source SHA-256: 7172aa7f22c18696389ad882d485b6a5cecf36004b0dbf08290e6347dc7bb71a
using RocheScoreboard.Mac.Native;
using Avalonia;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Roche_Scoreboard.Services.Cloud;

/// <summary>
/// Generates the short, easily-typed join codes shown to remote scorers.
///
/// We avoid the visually ambiguous characters <c>0/O/1/I/L</c> so the code
/// can be read from a projector or whiteboard without dictation errors.
/// The format is two groups of four characters separated by a hyphen,
/// e.g. <c>K7HM-2QPR</c>, giving roughly 28 bits of entropy — far more than
/// is needed for a one-game session, and short enough to type quickly.
/// </summary>
public static class JoinCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int GroupLength = 4;

    public static string Generate() =>
        $"{RandomGroup(GroupLength)}-{RandomGroup(GroupLength)}";

    private static string RandomGroup(int length)
    {
        Span<byte> buffer = stackalloc byte[length];
        RandomNumberGenerator.Fill(buffer);

        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(Alphabet[buffer[i] % Alphabet.Length]);
        return sb.ToString();
    }

    /// <summary>
    /// Generates the per-session host secret. Returned as a 32-character
    /// URL-safe base64 string with ~192 bits of entropy. It is stored once
    /// (in the operator's local config) and never displayed.
    /// </summary>
    public static string GenerateHostKey()
    {
        Span<byte> buffer = stackalloc byte[24];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
