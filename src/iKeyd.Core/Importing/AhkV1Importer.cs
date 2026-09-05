using System.Text.RegularExpressions;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;

namespace iKeyd.Core.Importing;

/// <summary>
/// Narrow migration importer for the AHK v1 declaration patterns used heavily by hotkeySKG.
/// This intentionally does not execute or attempt to compile arbitrary AutoHotkey code.
/// </summary>
public sealed class AhkV1Importer
{
    public const string UnsupportedStatementCode = "AHK9000";

    private static readonly Regex SingleStrokePattern = new(
        @"^\s*singleStroke(?<mode>[A-Za-z0-9_]+)_(?<key>[A-Za-z0-9_]+)\s*=\s*(?<output>.*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex ChordPattern = new(
        @"^\s*kCmb(?<mode>[A-Za-z]+)(?<ordinal>\d+)\s*:=\s*flag_(?<first>[A-Za-z0-9_]+)\s*\|\s*flag_(?<second>[A-Za-z0-9_]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ChordResultPattern = new(
        @"^\s*resultOfKCmb(?<mode>[A-Za-z]+)(?<ordinal>\d+)\s*=\s*(?<output>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FlagPattern = new(
        @"^\s*flag_[A-Za-z0-9_]+\s*:=.+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex HotkeyPattern = new(
        @"^\s*(?<trigger>[^;\s].*?)::(?<action>.*)$",
        RegexOptions.CultureInvariant);

    private sealed record SingleDeclaration(string Mode, string Key, string Output, int Line);
    private sealed record ChordDeclaration(string Mode, string Ordinal, string First, string Second, int Line);
    private sealed record ResultDeclaration(string Output, int Line);

    public AhkV1ImportResult Import(string source, int chordWindowMs = ChordEngine<string>.DefaultChordWindowMs)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (chordWindowMs < 0)
            throw new ArgumentOutOfRangeException(nameof(chordWindowMs));

        var diagnostics = new List<ImportDiagnostic>();
        var singles = new List<SingleDeclaration>();
        var chords = new List<ChordDeclaration>();
        var chordResults = new Dictionary<string, ResultDeclaration>(StringComparer.OrdinalIgnoreCase);
        var hotkeys = new List<HotkeyBinding>();
        var hotkeyTriggers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';'))
                continue;

            if (SingleStrokePattern.Match(line) is { Success: true } single)
            {
                singles.Add(new SingleDeclaration(
                    single.Groups["mode"].Value,
                    single.Groups["key"].Value,
                    single.Groups["output"].Value,
                    lineNumber));
                continue;
            }

            if (ChordPattern.Match(line) is { Success: true } chord)
            {
                chords.Add(new ChordDeclaration(
                    chord.Groups["mode"].Value,
                    chord.Groups["ordinal"].Value,
                    chord.Groups["first"].Value,
                    chord.Groups["second"].Value,
                    lineNumber));
                continue;
            }

            if (ChordResultPattern.Match(line) is { Success: true } chordResult)
            {
                var key = ChordResultKey(chordResult.Groups["mode"].Value, chordResult.Groups["ordinal"].Value);
                if (chordResults.TryGetValue(key, out var previous))
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Warning,
                        "AHK2003",
                        lineNumber,
                        $"Chord result '{key}' is assigned more than once; the last assignment wins (previous line {previous.Line}).",
                        line));
                }

                chordResults[key] = new ResultDeclaration(chordResult.Groups["output"].Value, lineNumber);
                continue;
            }

            if (FlagPattern.IsMatch(line))
                continue;

            if (HotkeyPattern.Match(line) is { Success: true } hotkey)
            {
                var trigger = hotkey.Groups["trigger"].Value.Trim();
                var actionText = hotkey.Groups["action"].Value.Trim();
                if (actionText.Length == 0)
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Warning,
                        "AHK1004",
                        lineNumber,
                        $"Multi-line hotkey '{trigger}::' is not imported; only single-line hotkeys are supported.",
                        line));
                    continue;
                }

                if (hotkeyTriggers.TryGetValue(trigger, out var previousLine))
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Warning,
                        "AHK2004",
                        lineNumber,
                        $"Hotkey '{trigger}' is declared more than once (previous line {previousLine}).",
                        line));
                }
                hotkeyTriggers[trigger] = lineNumber;
                hotkeys.Add(new HotkeyBinding(trigger, actionText));
                continue;
            }

            if (trimmed.StartsWith("singleStroke", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    "AHK1001",
                    lineNumber,
                    "Malformed single-stroke mapping. Expected singleStroke<mode>_<key>=<output>.",
                    line));
                continue;
            }

            if (trimmed.StartsWith("kCmb", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("resultOfKCmb", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new ImportDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    "AHK1002",
                    lineNumber,
                    "Malformed chord mapping/result declaration.",
                    line));
                continue;
            }

            diagnostics.Add(new ImportDiagnostic(
                ImportDiagnosticSeverity.Info,
                UnsupportedStatementCode,
                lineNumber,
                "Statement is outside the initial AHK v1 importer subset and was not imported.",
                line));
        }

        var keymaps = BuildKeymaps(singles, chords, chordResults, diagnostics);
        var startupMode = keymaps.Any(item => string.Equals(item.Name, "S", StringComparison.OrdinalIgnoreCase))
            ? "S"
            : keymaps.FirstOrDefault()?.Name ?? "S";
        var profile = new AutomationProfile(chordWindowMs, keymaps, startupMode, hotkeys);
        return new AhkV1ImportResult(profile, diagnostics);
    }

    private static IReadOnlyList<AutomationKeymapProfile> BuildKeymaps(
        IReadOnlyList<SingleDeclaration> singles,
        IReadOnlyList<ChordDeclaration> chords,
        IReadOnlyDictionary<string, ResultDeclaration> chordResults,
        List<ImportDiagnostic> diagnostics)
    {
        var modeNames = singles.Select(item => item.Mode)
            .Concat(chords.Select(item => item.Mode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new List<AutomationKeymapProfile>();

        foreach (var mode in modeNames)
        {
            var effectiveSingles = new Dictionary<string, SingleDeclaration>(StringComparer.OrdinalIgnoreCase);
            foreach (var declaration in singles.Where(item => string.Equals(item.Mode, mode, StringComparison.OrdinalIgnoreCase)))
            {
                if (effectiveSingles.TryGetValue(declaration.Key, out var previous))
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Warning,
                        "AHK2001",
                        declaration.Line,
                        $"Single-stroke '{mode}.{declaration.Key}' overrides the assignment on line {previous.Line}; AHK last-write-wins semantics are preserved."));
                }
                effectiveSingles[declaration.Key] = declaration;
            }

            var singleMappings = effectiveSingles.Values
                .OrderBy(item => item.Line)
                .Select(item => new SingleMapping<string>(item.Key, item.Output))
                .ToArray();

            var chordMappings = new List<ChordMapping<string>>();
            var seenChords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var declaration in chords.Where(item => string.Equals(item.Mode, mode, StringComparison.OrdinalIgnoreCase)))
            {
                var resultKey = ChordResultKey(declaration.Mode, declaration.Ordinal);
                if (!chordResults.TryGetValue(resultKey, out var output))
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Error,
                        "AHK1003",
                        declaration.Line,
                        $"Chord '{resultKey}' has no matching resultOfKCmb assignment."));
                    continue;
                }

                var pairKey = CanonicalPair(declaration.First, declaration.Second);
                if (seenChords.TryGetValue(pairKey, out var previousLine))
                {
                    diagnostics.Add(new ImportDiagnostic(
                        ImportDiagnosticSeverity.Warning,
                        "AHK2002",
                        declaration.Line,
                        $"Chord pair '{declaration.First}+{declaration.Second}' duplicates line {previousLine}; declaration order is preserved so the first mapping remains effective."));
                }
                else
                {
                    seenChords[pairKey] = declaration.Line;
                }

                chordMappings.Add(new ChordMapping<string>(declaration.First, declaration.Second, output.Output));
            }

            result.Add(new AutomationKeymapProfile(mode, singleMappings, chordMappings));
        }

        return result;
    }

    private static string ChordResultKey(string mode, string ordinal)
        => $"{mode}{ordinal}";

    private static string CanonicalPair(string first, string second)
    {
        var pair = new[] { first.ToUpperInvariant(), second.ToUpperInvariant() };
        Array.Sort(pair, StringComparer.Ordinal);
        return string.Join("+", pair);
    }
}
