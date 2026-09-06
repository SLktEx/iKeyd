using System.Text;
using iKeyd.Core.Configuration;
using iKeyd.Core.Importing;

namespace iKeyd.Cli;

internal static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    internal static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Count == 0 || args[0] is "-h" or "--help" or "help")
            {
                PrintHelp(output);
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "check" => Check(args, output, error),
                "import" => Import(args, output, error),
                "build" => Build(args, output),
                _ => UnknownCommand(args[0], output, error)
            };
        }
        catch (Exception exception)
        {
            error.WriteLine($"error: {exception.Message}");
            return 2;
        }
    }

    private static int Check(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (args.Count != 2)
            throw new ArgumentException("usage: ikeyd check PROFILE.ikeyd | SOURCE.ahk");

        return IsIKeydSource(args[1])
            ? CheckIKeyd(args[1], output)
            : CheckAhk(args[1], output, error);
    }

    private static int CheckIKeyd(string path, TextWriter output)
    {
        var compiled = CompileIKeyd(path);
        output.WriteLine($"checked {compiled.InputPath}");
        PrintIKeydSummary(compiled.Document, output);
        return 0;
    }

    private static int Build(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count is < 2 or > 3)
            throw new ArgumentException("usage: ikeyd build PROFILE.ikeyd [OUTPUT_DIR]");
        if (!IsIKeydSource(args[1]))
            throw new ArgumentException("ikeyd build expects a .ikeyd source file.");

        var compiled = CompileIKeyd(args[1]);
        var outputDirectory = args.Count == 3
            ? Path.GetFullPath(args[2])
            : Path.Combine(
                Directory.GetCurrentDirectory(),
                "build",
                Path.GetFileNameWithoutExtension(compiled.InputPath));

        Directory.CreateDirectory(outputDirectory);
        var profileOutput = Path.Combine(outputDirectory, "GeneratedProfile.g.cs");
        var mouseOutput = Path.Combine(outputDirectory, "GeneratedMouseProfile.g.cs");
        WriteGenerated(profileOutput, compiled.GeneratedProfile);
        WriteGenerated(mouseOutput, compiled.GeneratedMouseProfile);

        output.WriteLine($"built {compiled.InputPath}");
        output.WriteLine($"wrote {profileOutput}");
        output.WriteLine($"wrote {mouseOutput}");
        PrintIKeydSummary(compiled.Document, output);
        return 0;
    }

    private static CompiledIKeyd CompileIKeyd(string path)
    {
        var inputPath = Path.GetFullPath(path);
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("iKeyd DSL source was not found.", inputPath);
        if (!IsIKeydSource(inputPath))
            throw new ArgumentException("Canonical iKeyd compilation requires a .ikeyd source file.", nameof(path));

        var document = IKeydDslDocumentParser.Parse(File.ReadAllText(inputPath), inputPath);
        var generatedProfile = TypedProfileCompiler.Compile(document.Profile);
        var generatedMouseProfile = TypedMouseProfileCompiler.Compile(document.Mouse);
        return new CompiledIKeyd(inputPath, document, generatedProfile, generatedMouseProfile);
    }

    private static void PrintIKeydSummary(IKeydDslDocument document, TextWriter output)
    {
        var profile = document.Profile;
        var singles = profile.Keymaps.Values.Sum(keymap => keymap.SingleMappings.Count);
        var chords = profile.Keymaps.Values.Sum(keymap => keymap.ChordMappings.Count);
        var behaviors = profile.Keymaps.Values.Sum(keymap => keymap.BehaviorMappings.Count);

        output.WriteLine(
            $"summary: keymaps={profile.Keymaps.Count}, singles={singles}, chords={chords}, " +
            $"behaviors={behaviors}, behavior_defs={profile.BehaviorDefinitions.Count}, " +
            $"targets={document.TargetExtensions.Count}");
    }

    private static int CheckAhk(string path, TextWriter output, TextWriter error)
    {
        var result = ImportSource(path);
        PrintDiagnostics(result.Diagnostics, output, error);
        PrintImportSummary(result.Profile, result.Diagnostics, output);
        return result.HasErrors ? 1 : 0;
    }

    private static int Import(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        if (args.Count != 3)
            throw new ArgumentException("usage: ikeyd import SOURCE.ahk OUTPUT.json");

        var result = ImportSource(args[1]);
        PrintDiagnostics(result.Diagnostics, output, error);
        PrintImportSummary(result.Profile, result.Diagnostics, output);
        if (result.HasErrors)
        {
            error.WriteLine("import aborted because error diagnostics were produced.");
            return 1;
        }

        var outputPath = Path.GetFullPath(args[2]);
        AutomationProfileJson.Save(result.Profile, outputPath);
        output.WriteLine($"wrote {outputPath}");
        return 0;
    }

    private static AhkV1ImportResult ImportSource(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("AHK source file was not found.", fullPath);

        return new AhkV1Importer().Import(File.ReadAllText(fullPath));
    }

    private static void PrintDiagnostics(
        IEnumerable<ImportDiagnostic> diagnostics,
        TextWriter output,
        TextWriter error)
    {
        foreach (var diagnostic in diagnostics)
        {
            var stream = diagnostic.Severity == ImportDiagnosticSeverity.Info ? output : error;
            stream.WriteLine(
                $"{diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Code} " +
                $"line {diagnostic.Line}: {diagnostic.Message}");
            if (!string.IsNullOrWhiteSpace(diagnostic.SourceText))
                stream.WriteLine($"  {diagnostic.SourceText.Trim()}");
        }
    }

    private static void PrintImportSummary(
        AutomationProfile profile,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        TextWriter output)
    {
        var singleCount = profile.Keymaps.Values.Sum(keymap => keymap.SingleMappings.Count);
        var chordCount = profile.Keymaps.Values.Sum(keymap => keymap.ChordMappings.Count);
        var errors = diagnostics.Count(item => item.Severity == ImportDiagnosticSeverity.Error);
        var warnings = diagnostics.Count(item => item.Severity == ImportDiagnosticSeverity.Warning);
        var unsupported = diagnostics.Count(item => item.Code == AhkV1Importer.UnsupportedStatementCode);

        output.WriteLine(
            $"summary: keymaps={profile.Keymaps.Count}, singles={singleCount}, chords={chordCount}, " +
            $"hotkeys={profile.Hotkeys.Count}, errors={errors}, warnings={warnings}, unsupported={unsupported}");
    }

    private static bool IsIKeydSource(string path)
        => Path.GetExtension(path).Equals(".ikeyd", StringComparison.OrdinalIgnoreCase);

    private static void WriteGenerated(string outputPath, string source)
        => File.WriteAllText(outputPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static int UnknownCommand(string command, TextWriter output, TextWriter error)
    {
        error.WriteLine($"Unknown command '{command}'.");
        PrintHelp(output);
        return 2;
    }

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine("iKeyd CLI");
        output.WriteLine();
        output.WriteLine("  ikeyd check PROFILE.ikeyd");
        output.WriteLine("      Parse and validate the canonical .ikeyd source with the normal static compilers.");
        output.WriteLine();
        output.WriteLine("  ikeyd build PROFILE.ikeyd [OUTPUT_DIR]");
        output.WriteLine("      Generate GeneratedProfile.g.cs and GeneratedMouseProfile.g.cs without a JSON hop.");
        output.WriteLine();
        output.WriteLine("  ikeyd check SOURCE.ahk");
        output.WriteLine("      Parse the supported legacy AHK v1 importer subset and print diagnostics.");
        output.WriteLine();
        output.WriteLine("  ikeyd import SOURCE.ahk OUTPUT.json");
        output.WriteLine("      Convert the supported AHK v1 subset into an optional compatibility JSON profile.");
    }

    private sealed record CompiledIKeyd(
        string InputPath,
        IKeydDslDocument Document,
        string GeneratedProfile,
        string GeneratedMouseProfile);
}
