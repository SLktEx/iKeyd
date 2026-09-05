using iKeyd.Core.Platform;
using iKeyd.Linux.Input;

namespace iKeyd.X11;

public sealed record X11BackendOptions(
    IReadOnlyList<string> KeyboardDevicePaths,
    string UInputPath = "/dev/uinput",
    bool GrabPhysicalKeyboards = true,
    string? DisplayName = null,
    string XclipCommand = "xclip")
{
    public static X11BackendOptions Detect()
    {
        var configured = Environment.GetEnvironmentVariable("IKEYD_INPUT_DEVICES");
        var devices = !string.IsNullOrWhiteSpace(configured)
            ? configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : LinuxInputDeviceDiscovery.DiscoverKeyboardDevices().ToArray();
        var uinput = Environment.GetEnvironmentVariable("IKEYD_UINPUT") ??
            (File.Exists("/dev/uinput") ? "/dev/uinput" : "/dev/input/uinput");
        return new X11BackendOptions(devices, uinput, DisplayName: Environment.GetEnvironmentVariable("DISPLAY"));
    }
}

public sealed record X11BackendProbeResult(
    bool HasDisplay,
    bool CanReadPhysicalKeyboard,
    bool CanUseUInput,
    bool HasXclip,
    BackendCapabilities Capabilities,
    IReadOnlyList<string> Notes);

public static class X11BackendProbe
{
    public static X11BackendProbeResult Probe(X11BackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var notes = new List<string>();
        var hasDisplay = !string.IsNullOrWhiteSpace(options.DisplayName ?? Environment.GetEnvironmentVariable("DISPLAY"));
        if (!hasDisplay) notes.Add("No X11 DISPLAY is configured.");

        var readable = options.KeyboardDevicePaths.Any(path => CanOpen(path, FileAccess.Read));
        if (!readable) notes.Add("No readable physical keyboard event device was found; set IKEYD_INPUT_DEVICES or grant access to /dev/input.");
        var uinput = CanOpen(options.UInputPath, FileAccess.Write);
        if (!uinput) notes.Add($"uinput is unavailable at '{options.UInputPath}'.");
        var xclip = CommandExists(options.XclipCommand);
        if (!xclip) notes.Add("Install xclip for command-based X11 clipboard integration.");

        var capabilities = new List<BackendCapability>();
        if (readable) capabilities.Add(BackendCapability.KeyboardInput);
        if (readable && uinput && options.GrabPhysicalKeyboards) capabilities.Add(BackendCapability.KeyboardSuppression);
        if (uinput)
        {
            capabilities.AddRange([
                BackendCapability.KeyboardOutput,
                BackendCapability.TextOutputAscii,
                BackendCapability.PointerRelative,
                BackendCapability.PointerButtons,
                BackendCapability.PointerScroll,
                BackendCapability.MediaKeys
            ]);
        }
        if (hasDisplay)
        {
            capabilities.AddRange([
                BackendCapability.PointerAbsolute,
                BackendCapability.WindowQuery,
                BackendCapability.WindowMoveResize,
                BackendCapability.WindowState,
                BackendCapability.WindowActivation,
                BackendCapability.WindowTopMost,
                BackendCapability.WindowOpacity
            ]);
        }
        if (hasDisplay && xclip)
        {
            capabilities.Add(BackendCapability.ClipboardRead);
            capabilities.Add(BackendCapability.ClipboardWrite);
            capabilities.Add(BackendCapability.ClipboardWatch);
        }

        return new X11BackendProbeResult(hasDisplay, readable, uinput, xclip, new BackendCapabilities(capabilities), notes);
    }

    private static bool CanOpen(string path, FileAccess access)
    {
        try { using var stream = new FileStream(path, FileMode.Open, access, FileShare.ReadWrite); return true; }
        catch { return false; }
    }

    internal static bool CommandExists(string command)
    {
        if (Path.IsPathRooted(command)) return File.Exists(command);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, command))
            .Any(File.Exists);
    }
}
