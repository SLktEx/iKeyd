using System.Text;
using iKeyd.Core.Chords;
using iKeyd.Core.Input;
using iKeyd.Windows.Input;

namespace iKeyd.App;

internal enum InputDiagnosticKind : byte
{
    Event,
    KeymapOutputKeys,
    KeymapOutputLegacy,
    LegacyVirtualScan,
    ChordTimeout,
    Reset,
    Exception,
    InvariantViolation
}

internal readonly record struct InputDiagnosticState(
    LayerModifiers LayerModifiers,
    byte LayerCount,
    bool LayerConsumed,
    int HeldLayerCount,
    int HeldPhysicalCount,
    KeyboardModifierMask PhysicalModifiers,
    int SuppressedKeyCount,
    ChordEngineState SChordState,
    ChordEngineState KChordState,
    KeymapMode? TimerMode,
    long TimerDueAt);

internal readonly record struct InputDiagnosticEntry(
    long Sequence,
    long TimestampMs,
    InputDiagnosticKind DiagnosticKind,
    ushort VirtualKey,
    ushort ScanCode,
    KeyEventKind EventKind,
    KeyEventOrigin Origin,
    KeyboardDisposition Disposition,
    InputDiagnosticState Before,
    InputDiagnosticState After,
    int PayloadLength,
    ulong PayloadFingerprint,
    int DetailCode);

/// <summary>
/// Fixed-size in-memory trace of recent input state transitions. It deliberately
/// stores key IDs and small numeric state only; logical output text is represented
/// by length + a non-cryptographic fingerprint and is never persisted automatically.
/// </summary>
internal sealed class InputDiagnosticsBuffer
{
    internal const int Capacity = 256;

    private readonly InputDiagnosticEntry[] _entries = new InputDiagnosticEntry[Capacity];
    private int _next;
    private int _count;
    private long _sequence;

    public int Count => _count;

    public void RecordEvent(
        KeyboardEvent keyboardEvent,
        InputDiagnosticState before,
        InputDiagnosticState after,
        KeyboardDisposition disposition)
        => Add(new InputDiagnosticEntry(
            ++_sequence,
            keyboardEvent.TimestampMs,
            InputDiagnosticKind.Event,
            keyboardEvent.Key.VirtualKey,
            keyboardEvent.Key.ScanCode,
            keyboardEvent.Kind,
            keyboardEvent.Origin,
            disposition,
            before,
            after,
            0,
            0,
            0));

    public void RecordOutput(
        long timestampMs,
        InputDiagnosticKind kind,
        InputDiagnosticState state,
        string output,
        int detailCode = 0)
        => Add(new InputDiagnosticEntry(
            ++_sequence,
            timestampMs,
            kind,
            0,
            0,
            KeyEventKind.Down,
            KeyEventOrigin.OwnInjected,
            KeyboardDisposition.Suppress,
            state,
            state,
            output.Length,
            Fingerprint(output),
            detailCode));

    public void RecordMarker(
        long timestampMs,
        InputDiagnosticKind kind,
        InputDiagnosticState before,
        InputDiagnosticState after,
        int detailCode = 0)
        => Add(new InputDiagnosticEntry(
            ++_sequence,
            timestampMs,
            kind,
            0,
            0,
            KeyEventKind.Down,
            KeyEventOrigin.Physical,
            KeyboardDisposition.Suppress,
            before,
            after,
            0,
            0,
            detailCode));

    public InputDiagnosticEntry[] Snapshot()
    {
        var result = new InputDiagnosticEntry[_count];
        var start = (_next - _count + Capacity) % Capacity;
        for (var index = 0; index < _count; index++)
            result[index] = _entries[(start + index) % Capacity];
        return result;
    }

    public string ExportText()
    {
        var entries = Snapshot();
        var text = new StringBuilder(entries.Length * 190 + 256);
        text.AppendLine("iKeyd input diagnostics");
        text.AppendLine("Privacy: no literal keymap output text is stored; payloads are length + fingerprint only.");
        text.AppendLine("seq\tts\tkind\tkey\tevent\tdisposition\tbefore\tafter\tpayload\tdetail");

        foreach (var entry in entries)
        {
            text.Append(entry.Sequence).Append('\t')
                .Append(entry.TimestampMs).Append('\t')
                .Append(entry.DiagnosticKind).Append('\t')
                .Append("vk=").Append(entry.VirtualKey.ToString("X2"))
                .Append("/sc=").Append(entry.ScanCode.ToString("X3")).Append('\t')
                .Append(entry.Origin).Append('/').Append(entry.EventKind).Append('\t')
                .Append(entry.Disposition).Append('\t');
            AppendState(text, entry.Before);
            text.Append('\t');
            AppendState(text, entry.After);
            text.Append('\t')
                .Append("len=").Append(entry.PayloadLength)
                .Append("/fp=").Append(entry.PayloadFingerprint.ToString("X16"))
                .Append('\t')
                .Append(entry.DetailCode)
                .AppendLine();
        }

        return text.ToString();
    }

    internal static ulong Fingerprint(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }

    private void Add(InputDiagnosticEntry entry)
    {
        _entries[_next] = entry;
        _next = (_next + 1) % Capacity;
        if (_count < Capacity)
            _count++;
    }

    private static void AppendState(StringBuilder text, InputDiagnosticState state)
    {
        text.Append("layers=").Append((int)state.LayerModifiers)
            .Append('/').Append(state.LayerCount)
            .Append(state.LayerConsumed ? "/consumed" : "/clean")
            .Append(",heldLayer=").Append(state.HeldLayerCount)
            .Append(",heldPhysical=").Append(state.HeldPhysicalCount)
            .Append(",mods=").Append((int)state.PhysicalModifiers)
            .Append(",suppressed=").Append(state.SuppressedKeyCount)
            .Append(",chord=").Append(state.SChordState).Append('/').Append(state.KChordState)
            .Append(",timer=").Append(state.TimerMode?.ToString() ?? "-")
            .Append('@').Append(state.TimerDueAt);
    }
}
