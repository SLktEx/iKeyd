using Xunit;

namespace iKeyd.Windows.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowsGlobalInputE2ECollection
{
    public const string Name = "Windows global input E2E";
}
