using System.Text.RegularExpressions;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;

namespace iKeyd.Importers.AutoHotkeyV1;

public sealed class AhkV1Importer
{
    private static readonly Regex SingleRegex = new(
        @"^singleStroke(?<mode>[A-Za-z0-9]+)_(?<key>[A-Za-z0-9_]+)\s*=\s*(?<output>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ChordRegex = new(
        @"^kCmb(?<mode>[A-Za-z0-9]+)(?<ordinal>\d+)\s*:=\s*flag_(?<first>[A-Za-z0-9_]+)\s*\|\s*flag_(?<second>[A-Za-z0-9_]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ChordResultRegex = new(
        @"^resultOfKCmb(?<mode>[A-Za-z0-9]+)(?<ordinal>\d+)\s*=\s*(?<output>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex WindowRegex = new(
        @"^(?:MaximalGT|SingleKeyWait)\s*:?=\s*(?<milliseconds>\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex StartupModeRegex = new(
        @"^gmode\s*:?=\s*(?<mode>[A-Za-z0-9]+)MODE\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex OneLineHotkeyRegex = new(
        @"^(?<trigger>.+?)\s*::\s*(?<action>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmptyHotkeyRegex = new(
        @"^(?<trigger>.+?)\s*::\s*(?:;.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public AhkV1ImportResult ImportFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("AHK source path must not be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("AutoHotkey v1 source file was not found.", path);

        return Import(File.ReadAllText(path));
    }

    public AhkV1ImportResult Import(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lines = NormalizeLines(source);
        var diagnostics = new List<ImportDiagnostic>();
        var singles = new Dictionary<string, List<SingleDeclaration>>(StringComparer.OrdinalIgnoreCase);
        var chords = new List<ChordDeclaration>();
        var chordResults = new Dictionary<string, ChordResultDeclaration>(StringComparer.OrdinalIgnoreCase);
        var hotkeys = new List<HotkeyBinding>();
        var windowValues = new List<(int Line, int Value)>();
        var startupMode = "S";
        string? hotkeyContext = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var original = lines[index];
            var trimmed = original.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';'))
                continue;

            if (TryUpdateHotkeyContext(trimmed, out var newContext))
            {
                hotkeyContext = newContext;
                continue;
            }

            if (SingleRegex.Match(trimmed) is { Success: true } singleMatch)
            {
                var mode = singleMatch.Groups["mode"].Value;
                var declaration = new SingleDeclaration(
                    singleMatch.Groups["key"].Value,
                    StripTrailingComment(singleMatch.Groups["output"].Value),
                    lineNumber,
                    original);
                GetOrCreate(singles, mode).Add(declaration);
                continue;
            }

            if (ChordRegex.Match(trimmed) is { Success: true } chordMatch)
            {
                chords.Add(new ChordDeclaration(
                    chordMatch.Groups["mode"].Value,
                    chordMatch.Groups["ordinal"].Value,
                    chordMatch.Groups["first"].Value,
                    chordMatch.Groups["second"].Value,
                    lineNumber,
                    original));
                continue;
            }

            if (ChordResultRegex.Match(trimmed) is { Success: true } resultMatch)
            {
                var key = ChordResultKey(resultMatch.Groups["mode"].Value, resultMatch.Groups["ordinal"].Value);
                chordResults[key] = new ChordResultDeclaration(
                    StripTrailingComment(resultMatch.Groups["output"].Value),
                    lineNumber,
                    original);
                continue;
            }

            if (WindowRegex.Match(trimmed) is { Success: true } windowMatch)
            {
                windowValues.Add((lineNumber, int.Parse(windowMatch.Groups["milliseconds"].Value)));
                continue;
            }

            if (StartupModeRegex.Match(trimmed) is { Success: true } startupMatch)
            {
                startupMode = startupMatch.Groups["mode"].Value.ToUpperInvariant();
                continue;
            }

            if (TryImportSimpleHotkey(
                    trimmed,
                    original,
                    lineNumber,
                    hotkeyContext,
                    hotkeys,
                    diagnostics))
            {
                continue;
            }
        }

        AddSingleDiagnostics(singles, diagnostics);
        AddWindowDiagnostics(windowValues, diagnostics);

        var chordMappings = BuildChordMappings(chords, chordResults, diagnostics);
        AddChordDuplicateDiagnostics(chordMappings, diagnostics);

        var modes = singles.Keys
            .Concat(chordMappings.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(mode => mode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (modes.Length == 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                "AHK0001",
                ImportDiagnosticSeverity.Error,
                "No single-stroke or chord keymap declarations were found.",
                0));
        }

        var keymaps = modes.Select(mode => new AutomationKeymapProfile(
            mode,
            singles.TryGetValue(mode, out var modeSingles)
                ? modeSingles.Select(item => new SingleMapping<string>(item.Key, item.Output))
                : [],
            chordMappings.TryGetValue(mode, out var modeChords)
                ? modeChords.Select(item => new ChordMapping<string>(item.First, item.Second, item.Output))
                : []));

        var chordWindow = windowValues.Count == 0
            ? ChordEngine<string>.DefaultChordWindowMs
            : windowValues[0].Value;

        var profile = new AutomationProfile(chordWindow, keymaps, startupMode, hotkeys);
        return new AhkV1ImportResult(profile, diagnostics.OrderBy(item => item.LineNumber).ThenBy(item => item.Code).ToArray());
    }

    private static bool TryImportSimpleHotkey(
        string trimmed,
        string original,
        int lineNumber,
        string? context,
        ICollection<HotkeyBinding> hotkeys,
        ICollection<ImportDiagnostic> diagnostics)
    {
        var oneLine = OneLineHotkeyRegex.Match(trimmed);
        if (oneLine.Success)
        {
            var trigger = oneLine.Groups["trigger"].Value.Trim();
            if (trigger.StartsWith('#'))
                return false;

            var action = StripTrailingComment(oneLine.Groups["action"].Value).Trim();
            if (action.Length == 0)
                return false;

            if (context is not null)
            {
                diagnostics.Add(new ImportDiagnostic(
                    "AHK3002",
                    ImportDiagnosticSeverity.Warning,
                    $"Conditional hotkey '{trigger}' under '{context}' was not imported because profile hotkeys are currently context-free.",
                    lineNumber,
                    original));
                return true;
            }

            hotkeys.Add(new HotkeyBinding(trigger, action));
            return true;
        }

        var empty = EmptyHotkeyRegex.Match(trimmed);
        if (!empty.Success)
            return false;

        var emptyTrigger = empty.Groups["trigger"].Value.Trim();
        if (emptyTrigger.StartsWith('#'))
            return false;

        diagnostics.Add(new ImportDiagnostic(
            "AHK3001",
            ImportDiagnosticSeverity.Warning,
            $"Multi-line hotkey '{emptyTrigger}' was not imported. Only one-line hotkey -> action mappings are supported by the initial importer.",
            lineNumber,
            original));
        return true;
    }

    private static Dictionary<string, List<ResolvedChordDeclaration>> BuildChordMappings(
        IEnumerable<ChordDeclaration> declarations,
        IReadOnlyDictionary<string, ChordResultDeclaration> results,
        ICollection<ImportDiagnostic> diagnostics)
    {
        var byMode = new Dictionary<string, List<ResolvedChordDeclaration>>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations)
        {
            if (!results.TryGetValue(ChordResultKey(declaration.Mode, declaration.Ordinal), out var result))
            {
                diagnostics.Add(new ImportDiagnostic(
                    "AHK2001",
                    ImportDiagnosticSeverity.Error,
                    $"Chord {declaration.Mode}{declaration.Ordinal} has no matching resultOfKCmb assignment.",
                    declaration.Line,
                    declaration.Source));
                continue;
            }

            GetOrCreate(byMode, declaration.Mode).Add(new ResolvedChordDeclaration(
                declaration.First,
                declaration.Second,
                result.Output,
                declaration.Line,
                declaration.Source));
        }

        return byMode;
    }

    private static void AddSingleDiagnostics(
        IReadOnlyDictionary<string, List<SingleDeclaration>> singles,
        ICollection<ImportDiagnostic> diagnostics)
    {
        foreach (var (mode, declarations) in singles)
        {
            foreach (var duplicate in declarations.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            {
                var effective = duplicate.Last();
                diagnostics.Add(new ImportDiagnostic(
                    "AHK1001",
                    ImportDiagnosticSeverity.Warning,
                    $"singleStroke{mode}_{duplicate.Key} is assigned {duplicate.Count()} times; AutoHotkey last-write semantics make line {effective.Line} effective.",
                    effective.Line,
                    effective.Source));
            }
        }
    }

    private static void AddChordDuplicateDiagnostics(
        IReadOnlyDictionary<string, List<ResolvedChordDeclaration>> chords,
        ICollection<ImportDiagnostic> diagnostics)
    {
        foreach (var (mode, declarations) in chords)
        {
            foreach (var duplicate in declarations
                         .GroupBy(item => CanonicalPair(item.First, item.Second), StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                var effective = duplicate.First();
                diagnostics.Add(new ImportDiagnostic(
                    "AHK1002",
                    ImportDiagnosticSeverity.Warning,
                    $"Keymap {mode} chord {effective.First}+{effective.Second} is declared {duplicate.Count()} times; legacy first-match semantics keep output '{effective.Output}'.",
                    effective.Line,
                    effective.Source));
            }
        }
    }

    private static void AddWindowDiagnostics(
        IReadOnlyList<(int Line, int Value)> values,
        ICollection<ImportDiagnostic> diagnostics)
    {
        if (values.Select(item => item.Value).Distinct().Count() <= 1)
            return;

        diagnostics.Add(new ImportDiagnostic(
            "AHK1003",
            ImportDiagnosticSeverity.Warning,
            $"Legacy timing declarations disagree ({string.Join(", ", values.Select(item => item.Value))} ms); the first value is used as the profile chord window.",
            values[0].Line));
    }

    private static bool TryUpdateHotkeyContext(string trimmed, out string? context)
    {
        if (!trimmed.StartsWith("#IfWinActive", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("#IfWinExist", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("#If ", StringComparison.OrdinalIgnoreCase))
        {
            context = null;
            return false;
        }

        var separator = trimmed.IndexOfAny([',', ' ']);
        if (separator < 0 || separator == trimmed.Length - 1)
        {
            context = null;
            return true;
        }

        var remainder = trimmed[(separator + 1)..].Trim();
        context = remainder.Length == 0 ? null : trimmed;
        return true;
    }

    private static string StripTrailingComment(string value)
    {
        var match = Regex.Match(value, @"^(?<value>.*?)(?:\s+;.*)?$");
        return match.Groups["value"].Value.TrimEnd();
    }

    private static string[] NormalizeLines(string source)
        => source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static string ChordResultKey(string mode, string ordinal)
        => $"{mode}:{ordinal}";

    private static string CanonicalPair(string first, string second)
        => string.Compare(first, second, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{first}\0{second}"
            : $"{second}\0{first}";

    private static List<T> GetOrCreate<T>(Dictionary<string, List<T>> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var list))
        {
            list = [];
            dictionary[key] = list;
        }
        return list;
    }

    private sealed record SingleDeclaration(string Key, string Output, int Line, string Source);
    private sealed record ChordDeclaration(string Mode, string Ordinal, string First, string Second, int Line, string Source);
    private sealed record ChordResultDeclaration(string Output, int Line, string Source);
    private sealed record ResolvedChordDeclaration(string First, string Second, string Output, int Line, string Source);
}
