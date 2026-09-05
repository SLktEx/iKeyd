using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class HostedWorkflowContractTests
{
    [Fact]
    public void Reference_executable_hash_matches_workflow_default()
    {
        Assert.Equal(
            "5492198ce403d796c8588b17419bce82a0e6de3961bb40896a875ee5dee359ea",
            LegacyExecutableScenarioRunner.ReferenceSha256);
    }
}
