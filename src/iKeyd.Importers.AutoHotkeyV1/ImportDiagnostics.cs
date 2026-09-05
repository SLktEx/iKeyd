namespace iKeyd.Importers.AutoHotkeyV1;

public enum ImportDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ImportDiagnostic(
    string Code,
    ImportDiagnosticSeverity Severity,
    string Message,
    int LineNumber,
    string? SourceText = null);

public sealed record AhkV1ImportResult(
    iKeyd.Core.Configuration.AutomationProfile Profile,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(item => item.Severity == ImportDiagnosticSeverity.Error);
}
