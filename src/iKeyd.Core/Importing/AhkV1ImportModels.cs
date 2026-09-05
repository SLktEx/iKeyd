using iKeyd.Core.Configuration;

namespace iKeyd.Core.Importing;

public enum ImportDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ImportDiagnostic(
    ImportDiagnosticSeverity Severity,
    string Code,
    int Line,
    string Message,
    string? SourceText = null);

public sealed record AhkV1ImportResult(
    AutomationProfile Profile,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Error);

    public int UnsupportedStatementCount
        => Diagnostics.Count(diagnostic => diagnostic.Code == AhkV1Importer.UnsupportedStatementCode);
}
