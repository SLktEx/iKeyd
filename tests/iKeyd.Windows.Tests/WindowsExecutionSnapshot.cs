using System.Runtime.InteropServices;
using System.Text;

namespace iKeyd.Windows.Tests;

internal static class WindowsExecutionSnapshot
{
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    public static IReadOnlyDictionary<string, string> Capture(IReadOnlyList<string> scenarioModifiers)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Dictionary<string, string>
            {
                ["foregroundWindow"] = "unsupported-platform",
                ["physicalModifiers"] = "unsupported-platform",
                ["scenarioModifiers"] = string.Join(",", scenarioModifiers)
            };
        }

        return new Dictionary<string, string>
        {
            ["foregroundWindow"] = DescribeForegroundWindow(),
            ["physicalModifiers"] = DescribePhysicalModifiers(),
            ["scenarioModifiers"] = scenarioModifiers.Count == 0
                ? "<none>"
                : string.Join(",", scenarioModifiers)
        };
    }

    private static string DescribeForegroundWindow()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0)
            return "<none>";

        var title = new StringBuilder(512);
        _ = NativeMethods.GetWindowTextW(window, title, title.Capacity);

        var className = new StringBuilder(256);
        _ = NativeMethods.GetClassNameW(window, className, className.Capacity);

        return $"handle=0x{window.ToInt64():X};class={className};title={title}";
    }

    private static string DescribePhysicalModifiers()
        => string.Join(
            ";",
            $"Shift={IsDown(VkShift)}",
            $"Ctrl={IsDown(VkControl)}",
            $"Alt={IsDown(VkMenu)}",
            $"LWin={IsDown(VkLWin)}",
            $"RWin={IsDown(VkRWin)}");

    private static bool IsDown(int virtualKey)
        => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(nint window, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(nint window, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int virtualKey);
    }
}
