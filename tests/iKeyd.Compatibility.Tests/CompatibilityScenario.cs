using System.Text.Json;

namespace iKeyd.Compatibility.Tests;

public sealed record CompatibilityScenario
{
    public required string Id { get; init; }
    public string? Description { get; init; }
    public required ScenarioInitialState InitialState { get; init; }
    public required List<ScenarioInputEvent> Input { get; init; }
    public required ScenarioExpected Expected { get; init; }
    public List<string> Tags { get; init; } = [];
}

public sealed record ScenarioInitialState
{
    public string Mode { get; init; } = "S";
    public string Ime { get; init; } = "unchanged";
    public List<string> Modifiers { get; init; } = [];
}

public sealed record ScenarioInputEvent
{
    public long AtMs { get; init; }
    public required string Kind { get; init; }
    public string? Key { get; init; }
}

public sealed record ScenarioExpected
{
    public string? Text { get; init; }
    public List<ObservedKeyEvent> Events { get; init; } = [];
}

public sealed record ObservedKeyEvent
{
    public required string Kind { get; init; }
    public required string Key { get; init; }
}

public sealed record ScenarioRunResult
{
    public required string Runner { get; init; }
    public required string ScenarioId { get; init; }
    public string? Text { get; init; }
    public List<ObservedKeyEvent> Events { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public interface ICompatibilityScenarioRunner
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<ScenarioRunResult> RunAsync(CompatibilityScenario scenario, CancellationToken cancellationToken = default);
}

public static class CompatibilityScenarioCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<CompatibilityScenario> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        var scenarios = Directory
            .EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(LoadFile)
            .ToArray();

        var duplicate = scenarios
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
            throw new InvalidDataException($"Duplicate scenario id: {duplicate.Key}");

        return scenarios;
    }

    public static CompatibilityScenario LoadFile(string path)
    {
        var scenario = JsonSerializer.Deserialize<CompatibilityScenario>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Could not deserialize compatibility scenario: {path}");

        Validate(scenario, path);
        return scenario;
    }

    private static void Validate(CompatibilityScenario scenario, string source)
    {
        if (string.IsNullOrWhiteSpace(scenario.Id))
            throw new InvalidDataException($"Scenario id is required: {source}");

        if (scenario.InitialState is null)
            throw new InvalidDataException($"initialState is required: {source}");

        if (scenario.Input is null || scenario.Input.Count == 0)
            throw new InvalidDataException($"input must contain at least one event: {source}");

        var previous = -1L;
        foreach (var input in scenario.Input)
        {
            if (input.AtMs < 0 || input.AtMs < previous)
                throw new InvalidDataException($"input timestamps must be non-negative and monotonic: {source}");

            if (!string.Equals(input.Kind, "keyDown", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(input.Kind, "keyUp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsupported input kind '{input.Kind}': {source}");

            if (string.IsNullOrWhiteSpace(input.Key))
                throw new InvalidDataException($"key is required for {input.Kind}: {source}");

            previous = input.AtMs;
        }

        var ime = scenario.InitialState.Ime;
        if (!string.Equals(ime, "on", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ime, "off", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ime, "unchanged", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported IME state '{ime}': {source}");
    }
}

public static class CompatibilityScenarioDiff
{
    public static IReadOnlyList<string> Compare(CompatibilityScenario scenario, ScenarioRunResult actual)
    {
        var differences = new List<string>();

        if (!string.Equals(scenario.Id, actual.ScenarioId, StringComparison.OrdinalIgnoreCase))
            differences.Add($"scenario id: expected '{scenario.Id}', actual '{actual.ScenarioId}'");

        if (!string.Equals(scenario.Expected.Text, actual.Text, StringComparison.Ordinal))
            differences.Add($"text: expected '{scenario.Expected.Text ?? "<null>"}', actual '{actual.Text ?? "<null>"}'");

        if (scenario.Expected.Events.Count != actual.Events.Count)
        {
            differences.Add($"event count: expected {scenario.Expected.Events.Count}, actual {actual.Events.Count}");
            return differences;
        }

        for (var i = 0; i < scenario.Expected.Events.Count; i++)
        {
            var expected = scenario.Expected.Events[i];
            var observed = actual.Events[i];
            if (!string.Equals(expected.Kind, observed.Kind, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(expected.Key, observed.Key, StringComparison.OrdinalIgnoreCase))
            {
                differences.Add(
                    $"event[{i}]: expected {expected.Kind}:{expected.Key}, actual {observed.Kind}:{observed.Key}");
            }
        }

        return differences;
    }
}
