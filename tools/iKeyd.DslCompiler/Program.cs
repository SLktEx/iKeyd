using System.Text;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: iKeyd.DslCompiler <profile.ikeyd> <GeneratedProfile.g.cs> <GeneratedMouseProfile.g.cs>");
    return 2;
}

try
{
    var inputPath = Path.GetFullPath(args[0]);
    var profileOutputPath = Path.GetFullPath(args[1]);
    var mouseOutputPath = Path.GetFullPath(args[2]);
    if (!File.Exists(inputPath))
        throw new FileNotFoundException("iKeyd DSL source was not found.", inputPath);

    var document = IKeydDslDocumentParser.Parse(File.ReadAllText(inputPath), inputPath);
    WriteGenerated(profileOutputPath, TypedProfileCompiler.Compile(document.Profile));
    WriteGenerated(mouseOutputPath, TypedMouseProfileCompiler.Compile(document.Mouse));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"iKeyd DSL compilation failed: {exception.Message}");
    return 1;
}

static void WriteGenerated(string outputPath, string source)
{
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}
