using System.Text.Json;

namespace iKeyd.LegacySpec.Tests;

public sealed class LegacyBehaviorSpecTests
{
    private static readonly LegacySpec Spec = LegacySpec.Load();

    [Fact]
    public void Chord_window_is_40ms_and_inclusive()
    {
        Assert.Equal(40, Spec.Source.ChordWindowMs);
        Assert.True(ReferenceResolver.IsChordCandidate(39, Spec.Source.ChordWindowMs));
        Assert.True(ReferenceResolver.IsChordCandidate(40, Spec.Source.ChordWindowMs));
        Assert.False(ReferenceResolver.IsChordCandidate(41, Spec.Source.ChordWindowMs));
    }

    [Fact]
    public void Snapshot_contains_all_effective_single_strokes_and_declared_chords()
    {
        Assert.Equal(54, Spec.SingleStroke.S.Count);
        Assert.Equal(42, Spec.SingleStroke.K.Count);
        Assert.Equal(114, Spec.Chords.S.Count);
        Assert.Equal(112, Spec.Chords.K.Count);
    }

    [Theory]
    [InlineData("S", "Q", "-")]
    [InlineData("S", "W", "ni")]
    [InlineData("K", "Q", "o")]
    [InlineData("K", "SColon", "to")]
    public void Representative_single_strokes_match_legacy(string mode, string key, string expected)
        => Assert.Equal(expected, ReferenceResolver.ResolveSingle(Spec, mode, key));

    [Theory]
    [InlineData("S", "K", "Q", "fa")]
    [InlineData("S", "Q", "K", "fa")]
    [InlineData("K", "K", "Q", "ti")]
    [InlineData("K", "Q", "K", "ti")]
    public void Chords_are_order_independent(string mode, string first, string second, string expected)
        => Assert.Equal(expected, ReferenceResolver.ResolveChord(Spec, mode, first, second));

    [Theory]
    [InlineData("SColon", "V", "nya")]
    [InlineData("F", "U", "she")]
    public void Conflicting_K_mode_duplicates_preserve_first_match_behavior(string first, string second, string expected)
        => Assert.Equal(expected, ReferenceResolver.ResolveChord(Spec, "K", first, second));

    [Fact]
    public void Known_conflicting_K_mode_duplicates_are_explicit_in_the_snapshot()
    {
        var duplicates = Spec.KnownQuirks.DuplicateChordPatterns.K;
        Assert.Equal(2, duplicates.Count);
        Assert.Contains(duplicates, x => SameKeys(x.Keys, "SColon", "V") && x.EffectiveOutput == "nya");
        Assert.Contains(duplicates, x => SameKeys(x.Keys, "F", "U") && x.EffectiveOutput == "she");
    }

    [Fact]
    public void Duplicate_Colon_flag_assignment_is_recorded_as_legacy_input()
    {
        var duplicate = Assert.Single(Spec.KnownQuirks.DuplicateFlagDefinitions);
        Assert.Equal("Colon", duplicate.Key);
        Assert.Equal(new[] { "1<<32", "1<<45" }, duplicate.Expressions);
    }

    [Fact]
    public void Undefined_chord_falls_back_to_first_single_stroke()
    {
        Assert.Null(ReferenceResolver.ResolveChord(Spec, "S", "Q", "W"));
        Assert.Equal("-", ReferenceResolver.ResolveSingle(Spec, "S", "Q"));
    }

    private static bool SameKeys(IReadOnlyList<string> actual, string a, string b)
        => new HashSet<string>(actual, StringComparer.OrdinalIgnoreCase).SetEquals([a, b]);
}

internal static class ReferenceResolver
{
    public static bool IsChordCandidate(int deltaMs, int windowMs) => deltaMs <= windowMs;

    public static string? ResolveSingle(LegacySpec spec, string mode, string key)
    {
        var map = mode.ToUpperInvariant() switch
        {
            "S" => spec.SingleStroke.S,
            "K" => spec.SingleStroke.K,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return map.FirstOrDefault(x => Eq(x.Key, key)).Value;
    }

    public static string? ResolveChord(LegacySpec spec, string mode, string first, string second)
    {
        var declarations = mode.ToUpperInvariant() switch
        {
            "S" => spec.Chords.S,
            "K" => spec.Chords.K,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return declarations.FirstOrDefault(x => SamePair(x, first, second))?[2];
    }

    private static bool SamePair(IReadOnlyList<string> chord, string first, string second)
        => chord.Count == 3 &&
           ((Eq(chord[0], first) && Eq(chord[1], second)) ||
            (Eq(chord[0], second) && Eq(chord[1], first)));

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

internal sealed record LegacySpec(SourceSpec Source, SingleStrokeModes SingleStroke, ChordModes Chords, KnownQuirks KnownQuirks)
{
    public static LegacySpec Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
        return JsonSerializer.Deserialize<LegacySpec>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Could not deserialize legacy behavior fixture.");
    }
}

internal sealed record SourceSpec(string Runtime, int ExecutableLines, int ChordWindowMs);
internal sealed record SingleStrokeModes(Dictionary<string, string> S, Dictionary<string, string> K);
internal sealed record ChordModes(List<List<string>> S, List<List<string>> K);
internal sealed record KnownQuirks(DuplicateChordModes DuplicateChordPatterns, List<DuplicateFlagDefinition> DuplicateFlagDefinitions);
internal sealed record DuplicateChordModes(List<DuplicateChordPattern> S, List<DuplicateChordPattern> K);
internal sealed record DuplicateChordPattern(List<string> Keys, List<string> Outputs, string EffectiveOutput);
internal sealed record DuplicateFlagDefinition(string Key, List<string> Expressions);
