namespace iKeyd.Linux.Input;

public static class LinuxInputDeviceDiscovery
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
        try { return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path); }
        catch { return Path.GetFullPath(path); }
    }
}
