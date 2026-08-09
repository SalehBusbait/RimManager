using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Every source file is UTF-8, carries no BOM, and is free of mojibake.
/// <para>
/// This exists because of a real regression that reached a screenshot. A tool round-trip
/// re-read a BOM-less UTF-8 file as Windows-1252 and wrote it back as UTF-8, so every
/// em dash, ellipsis and <c>⎇</c> in the Settings window rendered as a three-character
/// Latin-1 smear. The build stayed at 0 warnings, every test passed and the app launched
/// clean — double-encoded text is still perfectly valid UTF-8, so nothing in the pipeline
/// has anything to object to. The only symptom was garbage on screen.
/// </para>
/// <para>
/// The BOM half matters too. Windows PowerShell 5.1 reads a BOM-less file as the system
/// ANSI codepage and writes UTF-8 <i>with</i> a BOM, which is exactly the round trip that
/// caused this; a BOM appearing in the tree is the fingerprint of that having happened.
/// </para>
/// </summary>
public sealed class SourceEncodingTests
{
    /// <summary>
    /// Two-character sequences that occur only when UTF-8 has been read as a single-byte
    /// codepage and re-encoded: the lead byte of a UTF-8 punctuation or Latin-1 sequence
    /// lands on U+00E2 / U+00C2 / U+00C3, and a re-encoded BOM on U+00EF U+00BB U+00BF.
    /// <para>
    /// Assembled from code points rather than written out, because a test that spells the
    /// mojibake it forbids fails on its own source.
    /// </para>
    /// </summary>
    private static readonly string[] MojibakeMarkers =
    [
        Marker(0x00E2, 0x20AC),          // em dash, en dash, ellipsis, curly quotes
        Marker(0x00C2, 0x00B7),          // the middot this UI uses as a separator
        Marker(0x00C3, 0x00A9),          // accented Latin letters
        Marker(0x00EF, 0x00BB, 0x00BF),  // a BOM that has itself been double-encoded
    ];

    private static string Marker(params int[] codePoints) =>
        string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static IEnumerable<string> SourceFiles()
    {
        foreach (var dir in new[] { "src", "tests" })
        {
            var root = Path.Combine(RepoPaths.Root, dir);
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(file) is not (".cs" or ".axaml" or ".csproj" or ".md" or ".json"))
                    continue;

                var sep = Path.DirectorySeparatorChar;
                if (file.Contains($"{sep}obj{sep}") || file.Contains($"{sep}bin{sep}")) continue;

                yield return file;
            }
        }
    }

    [Fact]
    public void No_source_file_carries_a_byte_order_mark()
    {
        var offenders = SourceFiles()
            .Where(f =>
            {
                var head = new byte[3];
                using var stream = File.OpenRead(f);
                return stream.Read(head) == 3 && head is [0xEF, 0xBB, 0xBF];
            })
            .Select(f => Path.GetRelativePath(RepoPaths.Root, f))
            .ToList();

        offenders.Should().BeEmpty(
            "a BOM means something rewrote the file with a tool that adds one — most often "
            + "PowerShell 5.1's Set-Content -Encoding utf8, which is also the thing that "
            + "double-encodes the contents on the way through");
    }

    [Fact]
    public void No_source_file_is_double_encoded()
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var relative = Path.GetRelativePath(RepoPaths.Root, file);

            string text;
            try
            {
                text = strict.GetString(File.ReadAllBytes(file));
            }
            catch (ArgumentException)
            {
                offenders.Add($"{relative} (not valid UTF-8)");
                continue;
            }

            for (var i = 0; i < MojibakeMarkers.Length; i++)
            {
                if (!text.Contains(MojibakeMarkers[i], StringComparison.Ordinal)) continue;

                offenders.Add($"{relative} (double-encoded; marker #{i})");
                break;
            }
        }

        offenders.Should().BeEmpty(
            "double-encoded text is still valid UTF-8, so nothing else in the pipeline "
            + "notices — the em dashes and the ⎇ glyph simply render as garbage");
    }
}
