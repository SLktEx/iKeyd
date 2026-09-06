using System.Text.Json;
using iKeyd.App;
using iKeyd.Core.Desktop;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class PinnedSendSyntaxInventoryTests
{
    [Fact]
    [Trait("Category", "HostedLegacyDifferentialE2E")]
    [Trait("Category", "HostedAhkSourceDifferentialE2E")]
    public void Every_finite_pinned_Send_expression_is_accepted_by_iKeyd_interpreter()
    {
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (string.IsNullOrWhiteSpace(workspace))
            return;

        var path = Path.Combine(
            workspace,
            "TestResults",
            "compatibility-inventory",
            "send-syntax-inventory.json");
        if (!File.Exists(path))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var expressions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var item in document.RootElement.GetProperty("expressions").EnumerateArray())
        {
            if (!item.GetProperty("dynamic").GetBoolean())
                expressions.Add(item.GetProperty("expression").GetString() ?? string.Empty);
        }

        var dynamicReachability = document.RootElement.GetProperty("dynamicReachability");
        foreach (var item in dynamicReachability.GetProperty("expressions").EnumerateArray())
        {
            if (!item.GetProperty("bounded").GetBoolean())
                continue;

            foreach (var reachable in item.GetProperty("reachableExpressions").EnumerateArray())
                expressions.Add(reachable.GetProperty("expression").GetString() ?? string.Empty);
        }

        Assert.NotEmpty(expressions);

        var failures = new List<string>();
        foreach (var expression in expressions)
        {
            try
            {
                var output = new LegacySendOutput(new ProbeKeyboardOutput(), new ProbeDesktopBackend());
                output.Send(expression);
            }
            catch (Exception error)
            {
                failures.Add($"{Escape(expression)} -> {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            failures.Count == 0
                ? string.Empty
                : $"{failures.Count} finite pinned Send expressions were rejected:{Environment.NewLine}" +
                  string.Join(Environment.NewLine, failures));
    }

    private static string Escape(string value)
        => value.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private sealed class ProbeKeyboardOutput : IKeyboardOutput
    {
        public void SendKey(KeyboardKey key, KeyEventKind kind) { }
        public void SendKeyPress(KeyboardKey key) { }
        public void SendText(string text) { }
        public bool IsToggleOn(ushort virtualKey) => false;
    }

    private sealed class ProbeDesktopBackend : IDesktopBackend
    {
        private DesktopPoint _pointer;
        private readonly HashSet<DesktopMouseButton> _down = [];

        public WindowHandle GetActiveWindow() => new(1);
        public DesktopWindowState GetWindowState(WindowHandle window) => DesktopWindowState.Normal;
        public DesktopRect GetWindowBounds(WindowHandle window) => new(0, 0, 100, 100);
        public DesktopRect GetPrimaryWorkArea() => new(0, 0, 1920, 1080);
        public string? GetWindowClass(WindowHandle window) => "SendSyntaxProbe";
        public bool IsWindow(WindowHandle window) => true;
        public void Minimize(WindowHandle window) { }
        public void Maximize(WindowHandle window) { }
        public void Restore(WindowHandle window) { }
        public void MoveResize(WindowHandle window, DesktopRect bounds) { }
        public void Activate(WindowHandle window) { }
        public IReadOnlyList<WindowHandle> EnumerateTopLevelWindows() => [new WindowHandle(1)];
        public bool IsTopMost(WindowHandle window) => false;
        public void SetTopMost(WindowHandle window, bool enabled) { }
        public byte? GetOpacity(WindowHandle window) => null;
        public void SetOpacity(WindowHandle window, byte? opacity) { }
        public bool HasCaption(WindowHandle window) => true;
        public void SetCaption(WindowHandle window, bool enabled) { }
        public DesktopPoint GetPointerPosition() => _pointer;
        public void MovePointer(DesktopPoint position) => _pointer = position;
        public void MovePointerBy(int deltaX, int deltaY)
            => _pointer = new DesktopPoint(_pointer.X + deltaX, _pointer.Y + deltaY);
        public bool IsMouseButtonDown(DesktopMouseButton button) => _down.Contains(button);
        public void SetMouseButton(DesktopMouseButton button, bool down)
        {
            if (down) _down.Add(button); else _down.Remove(button);
        }
        public void Click(DesktopMouseButton button) { }
        public void ScrollVertical(int wheelDelta, bool controlModifier = false) { }
        public void SendMediaCommand(DesktopMediaCommand command) { }
    }
}
