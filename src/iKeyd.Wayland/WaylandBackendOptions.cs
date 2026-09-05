using iKeyd.Core.Platform;

namespace iKeyd.Wayland;

public sealed record WaylandBackendOptions(
    IReadOnlyList<string> KeyboardDevicePaths,
    string UInputPath = "/dev/uinput",
    bool GrabPhysicalKeyboards = true,
    string WlCopyCommand = "wl-copy",
    string WlPasteCommand = "wl-paste")
{
    public static WaylandBackendOptions Detect()
    {
        var configured = Environment.GetEnvironmentVariable("IKEYD_INPUT_DEVICES");
        var devices = !string.IsNullOrWhiteSpace(configured)
            ? configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : WaylandInputDeviceDiscovery.DiscoverKeyboardDevices().ToArray();

        var uinput = Environment.GetEnvironmentVariable("IKEYD_UINPUT") ?? DetectUInputPath();
        return new WaylandBackendOptions(devices, uinput);
    }

    private static string DetectUInputPath()
        => File.Exists("/dev/uinput") ? "/dev/uinput" : "/dev/input/uinput";
}

public static class WaylandInputDeviceDiscovery
{
    public static IEnumerable<string> DiscoverKeyboardDevices()
    {
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directory in new[] { "/dev/input/by-id", "/dev/input/by-path" })
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory, "*-event-kbd").OrderBy(path => path, StringComparer.Ordinal))
            {
                var target = ResolveDeviceTarget(path);
                if (seenTargets.Add(target))
                    yield return path;
            }
        }
    }

    private static string ResolveDeviceTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);
        }
        catch
        {
            return Path.GetFullPath(path);
        }
    }
}

public sealed record WaylandBackendProbeResult(
    bool IsWaylandSession,
    bool CanReadPhysicalKeyboard,
    bool CanUseUInput,
    bool HasWlCopy,
    bool HasWlPaste,
    BackendCapabilities Capabilities,
    IReadOnlyList<string> Notes);

public static class WaylandBackendProbe
{
    public static WaylandBackendProbeResult Probe(WaylandBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var notes = new List<string>();
        var isWayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) ||
                        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        if (!isWayland)
            notes.Add("No Wayland session was detected (XDG_SESSION_TYPE/WAYLAND_DISPLAY).");

        var readable = options.KeyboardDevicePaths.Count > 0 && options.KeyboardDevicePaths.Any(CanReadFile);
        if (!readable)
            notes.Add("No readable physical keyboard event device was found; set IKEYD_INPUT_DEVICES or grant access to /dev/input.");

        var uinput = CanWriteFile(options.UInputPath);
        if (!uinput)
            notes.Add($"uinput is unavailable at '{options.UInputPath}'; grant access to /dev/uinput or set IKEYD_UINPUT.");

        var wlCopy = CommandSearch.Exists(options.WlCopyCommand);
        var wlPaste = CommandSearch.Exists(options.WlPasteCommand);
        if (!wlCopy || !wlPaste)
            notes.Add("Install wl-clipboard (wl-copy/wl-paste) for the portable command-based clipboard integration.");

        var capabilities = new List<BackendCapability>();
        if (readable)
            capabilities.Add(BackendCapability.KeyboardInput);
        if (readable && uinput && options.GrabPhysicalKeyboards)
            capabilities.Add(BackendCapability.KeyboardSuppression);
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
        if (isWayland && wlPaste)
        {
            capabilities.Add(BackendCapability.ClipboardRead);
            capabilities.Add(BackendCapability.ClipboardWatch);
        }
        if (isWayland && wlCopy)
            capabilities.Add(BackendCapability.ClipboardWrite);

        return new WaylandBackendProbeResult(
            isWayland,
            readable,
            uinput,
            wlCopy,
            wlPaste,
            new BackendCapabilities(capabilities),
            notes);
    }

    private static bool CanReadFile(string path) => CanOpen(path, FileAccess.Read);
    private static bool CanWriteFile(string path) => CanOpen(path, FileAccess.Write);

    private static bool CanOpen(string path, FileAccess access)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, access, FileShare.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal static class CommandSearch
{
    public static bool Exists(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;
        if (Path.IsPathRooted(command))
            return File.Exists(command);

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, command))
            .Any(File.Exists);
    }
}
