using Xunit;

namespace iKeyd.Compatibility.Tests;

public sealed class CompatibilityScenarioTests
{
    private static string ScenarioDirectory => Path.Combine(AppContext.BaseDirectory, "Scenarios");

    [Fact]
    public void Scenario_catalog_loads_valid_scenarios_with_monotonic_timing()
    {
        var scenarios = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory);

        Assert.NotEmpty(scenarios);
        Assert.All(scenarios, scenario =>
        {
            Assert.False(string.IsNullOrWhiteSpace(scenario.Id));
            Assert.NotEmpty(scenario.Input);
            Assert.Equal(scenario.Input.OrderBy(input => input.AtMs).ToArray(), scenario.Input);
        });
    }

    [Fact]
    public void Scenario_ids_are_unique()
    {
        var scenarios = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory);

        Assert.Equal(
            scenarios.Count,
            scenarios.Select(scenario => scenario.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Diff_is_empty_when_runner_matches_expected_observations()
    {
        var scenario = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Single(item => item.Id == "s-chord-k-q-at-40ms");

        var result = new ScenarioRunResult
        {
            Runner = "test",
            ScenarioId = scenario.Id,
            Text = scenario.Expected.Text,
            Events = scenario.Expected.Events.ToList()
        };

        Assert.Empty(CompatibilityScenarioDiff.Compare(scenario, result));
    }

    [Fact]
    public void Diff_reports_text_and_event_mismatches()
    {
        var scenario = CompatibilityScenarioCatalog.LoadDirectory(ScenarioDirectory)
            .Single(item => item.Id == "s-chord-k-q-at-40ms");

        var result = new ScenarioRunResult
        {
            Runner = "test",
            ScenarioId = scenario.Id,
            Text = "wrong",
            Events =
            [
                new ObservedKeyEvent { Kind = "keyDown", Key = "Q" }
            ]
        };

        var differences = CompatibilityScenarioDiff.Compare(scenario, result);

        Assert.Contains(differences, difference => difference.StartsWith("text:", StringComparison.Ordinal));
        Assert.Contains(differences, difference => difference.StartsWith("event count:", StringComparison.Ordinal));
    }
}
