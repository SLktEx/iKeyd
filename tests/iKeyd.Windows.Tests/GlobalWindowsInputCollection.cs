using Xunit;

namespace iKeyd.Windows.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalWindowsInputCollection
{
    public const string Name = "Global Windows input";
}
