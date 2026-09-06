using System.Text;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: iKeyd.DslCompiler <profile.ikeyd> <GeneratedProfile.g.cs>");
    return 2;
}

try
{
    var inputPath = Path.GetFullPath(args[0]);
    var outputPath = Path.GetFullPath(args[1]);
    if (!File.Exists(inputPath))
        throw new FileNotFoundException("iKeyd DSL source was not found.", inputPath);

    var generatedSource = TypedProfileCompiler.CompileFile(inputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, generatedSource, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"iKeyd DSL compilation failed: {exception.Message}");
    return 1;
}
