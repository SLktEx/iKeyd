using CliProgram = iKeyd.Cli.Program;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class IKeydCliTests
{
    [Fact]
    public void Check_ikeyd_uses_the_canonical_compiler_without_writing_outputs()
    {
        var source = FixturePath("hotkeySKG.ikeyd");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CliProgram.Run(["check", source], output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.Contains("checked ", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("keymaps=", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("behaviors=", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ikeyd_emits_the_same_static_sources_as_the_normal_generators()
    {
        var source = FixturePath("hotkeySKG.ikeyd");
        var outputDirectory = TempDirectory();
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = CliProgram.Run(["build", source, outputDirectory], output, error);

            Assert.Equal(0, exitCode);
            Assert.Empty(error.ToString());

            var document = IKeydDslDocumentParser.Parse(File.ReadAllText(source), source);
            Assert.Equal(
                TypedProfileCompiler.Compile(document.Profile),
                File.ReadAllText(Path.Combine(outputDirectory, "GeneratedProfile.g.cs")));
            Assert.Equal(
                TypedMouseProfileCompiler.Compile(document.Mouse),
                File.ReadAllText(Path.Combine(outputDirectory, "GeneratedMouseProfile.g.cs")));
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void Invalid_ikeyd_returns_nonzero_with_the_compiler_diagnostic()
    {
        var directory = TempDirectory();
        var source = Path.Combine(directory, "invalid.ikeyd");
        File.WriteAllText(source, """
            profile demo {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                A = UNICODE() {
                    value = "ab"
                }
            }

            keymap K {
                A = "a"
            }
            """);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = CliProgram.Run(["check", source], output, error);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("error:", error.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UNICODE", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Check_ahk_keeps_the_existing_importer_route()
    {
        var directory = TempDirectory();
        var source = Path.Combine(directory, "legacy.ahk");
        File.WriteAllText(source, """
            flag_Q:=1<<1
            flag_W:=1<<2
            singleStrokeS_Q=ka
            singleStrokeS_W=na
            kCmbS1:=flag_Q|flag_W
            resultOfKCmbS1=kya
            """);

        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = CliProgram.Run(["check", source], output, error);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("iKeyd DSL source", error.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("singles=2", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("chords=1", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Help_describes_both_canonical_and_legacy_command_surfaces()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CliProgram.Run(["--help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.Contains("ikeyd check PROFILE.ikeyd", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("ikeyd build PROFILE.ikeyd", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("ikeyd import SOURCE.ahk", output.ToString(), StringComparison.Ordinal);
    }

    private static string FixturePath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ikeyd-cli-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
