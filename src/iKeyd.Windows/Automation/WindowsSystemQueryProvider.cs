using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using iKeyd.Core.Automation;
using iKeyd.Core.Input;

namespace iKeyd.Windows.Automation;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemQueryProvider : ISystemQueryProvider
{
    private const int VkCapsLock = 0x14;
    private const int VkNumLock = 0x90;
    private const int VkScrollLock = 0x91;

    private readonly IInputMethod _inputMethod;

    public WindowsSystemQueryProvider(IInputMethod inputMethod)
    {
        _inputMethod = inputMethod ?? throw new ArgumentNullException(nameof(inputMethod));
    }

    public string GetValue(string key)
    {
        var normalized = SystemQueryKeys.Normalize(key);
        return normalized switch
        {
            SystemQueryKeys.Os => RuntimeInformation.OSDescription,
            SystemQueryKeys.Architecture => RuntimeInformation.OSArchitecture.ToString(),
            SystemQueryKeys.Hostname => Environment.MachineName,
            SystemQueryKeys.Username => Environment.UserName,
            SystemQueryKeys.ForegroundProcess => GetForegroundProcessName(),
            SystemQueryKeys.ForegroundPid => GetForegroundProcessId().ToString(System.Globalization.CultureInfo.InvariantCulture),
            SystemQueryKeys.ForegroundTitle => GetForegroundWindowTitle(),
            SystemQueryKeys.ImeKanaActive => Bool(_inputMethod.IsKanaInputActive()),
            SystemQueryKeys.KeyboardCapsLock => Bool(IsLockKeyEnabled(VkCapsLock)),
            SystemQueryKeys.KeyboardNumLock => Bool(IsLockKeyEnabled(VkNumLock)),
            SystemQueryKeys.KeyboardScrollLock => Bool(IsLockKeyEnabled(VkScrollLock)),
            _ => throw new ArgumentOutOfRangeException(nameof(key), normalized, "Unsupported Windows system query.")
        };
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static uint GetForegroundProcessId()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0)
            return 0;

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    private static string GetForegroundProcessName()
    {
        var processId = GetForegroundProcessId();
        if (processId == 0)
            return string.Empty;

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var name = process.ProcessName;
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string GetForegroundWindowTitle()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0)
            return string.Empty;

        var length = NativeMethods.GetWindowTextLengthW(window);
        if (length <= 0)
            return string.Empty;

        var buffer = new StringBuilder(length + 1);
        return NativeMethods.GetWindowTextW(window, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }

    private static bool IsLockKeyEnabled(int virtualKey)
        => (NativeMethods.GetKeyState(virtualKey) & 0x0001) != 0;

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLengthW(nint window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(nint window, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int virtualKey);
    }
}
