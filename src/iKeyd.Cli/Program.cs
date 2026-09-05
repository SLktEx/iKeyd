using iKeyd.Core.Configuration;
using iKeyd.Core.Importing;

namespace iKeyd.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 2;
        }
    }

    private static int Run(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "check" => Check(args),
            "import" => Import(args),
            "build" => ReservedBuild(),
            _ => UnknownCommand(args[0])
        };
    }

    private static int Check(IReadOnlyList<string> args)
    {
        if (args.Count != 2)
            throw new ArgumentException("usage: ikeyd check SOURCE.ahk");

        var result = ImportSource(args[1]);
        PrintDiagnostics(result.Diagnostics);
        PrintSummary(result.Profile, result.Diagnostics);
        return result.HasErrors ? 1 : 0;
    }

    private static int Import(IReadOnlyList<string> args)
    {
        if (args.Count != 3)
            throw new ArgumentException("usage: ikeyd import SOURCE.ahk OUTPUT.json");

        var result = ImportSource(args[1]);
        PrintDiagnostics(result.Diagnostics);
        PrintSummary(result.Profile, result.Diagnostics);
        if (result.HasErrors)
        {
            Console.Error.WriteLine("import aborted because error diagnostics were produced.");
            return 1;
        }

        var output = Path.GetFullPath(args[2]);
        AutomationProfileJson.Save(result.Profile, output);
        Console.WriteLine($"wrote {output}");
        return 0;
    }

    private static AhkV1ImportResult ImportSource(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("AHK source file was not found.", fullPath);

        var source = File.ReadAllText(fullPath);
        return new AhkV1Importer().Import(source);
    }

    private static void PrintDiagnostics(IEnumerable<ImportDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var stream = diagnostic.Severity == ImportDiagnosticSeverity.Info ? Console.Out : Console.Error;
            stream.WriteLine($"{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Code} line {diagnostic.Line}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.SourceText))
                stream.WriteLine($"  {diagnostic.SourceText.Trim()}");
        }
    }

    private static void PrintSummary(
        AutomationProfile profile,
        IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        var singleCount = profile.Keymaps.Values.Sum(keymap => keymap.SingleMappings.Count);
        var chordCount = profile.Keymaps.Values.Sum(keymap => keymap.ChordMappings.Count);
        var errors = diagnostics.Count(item => item.Severity == ImportDiagnosticSeverity.Error);
        var warnings = diagnostics.Count(item => item.Severity == ImportDiagnosticSeverity.Warning);
        var unsupported = diagnostics.Count(item => item.Code == AhkV1Importer.UnsupportedStatementCode);

        Console.WriteLine(
            $"summary: keymaps={profile.Keymaps.Count}, singles={singleCount}, chords={chordCount}, " +
            $"hotkeys={profile.Hotkeys.Count}, errors={errors}, warnings={warnings}, unsupported={unsupported}");
    }

    private static int ReservedBuild()
    {
        Console.Error.WriteLine("The 'build' command is reserved for a future compiler/package pipeline. Use 'check' or 'import' today.");
        return 2;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("iKeyd migration CLI");
        Console.WriteLine();
        Console.WriteLine("  ikeyd check SOURCE.ahk");
        Console.WriteLine("      Parse the supported AHK v1 subset and print diagnostics.");
        Console.WriteLine();
        Console.WriteLine("  ikeyd import SOURCE.ahk OUTPUT.json");
        Console.WriteLine("      Convert supported single-stroke/chord/simple-hotkey declarations into an iKeyd AutomationProfile.");
        Console.WriteLine();
        Console.WriteLine("  ikeyd build ...");
        Console.WriteLine("      Reserved for the future build/package pipeline.");
    }
}
