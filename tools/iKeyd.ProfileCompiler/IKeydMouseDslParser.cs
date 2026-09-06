using System.Globalization;
using System.Text.RegularExpressions;
using iKeyd.Core.Configuration;

internal static class IKeydMouseDslParser
{
    public static MouseDslExtraction Extract(string text, string sourcePath = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(text);
        var path = string.IsNullOrWhiteSpace(sourcePath) ? "<memory>" : sourcePath;
        var lines = text.Split('\n');
        var foundStart = -1;
        var foundEnd = -1;
        var depth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComment(lines[index].TrimEnd('\r')).Trim();
            if (depth == 0 && Regex.IsMatch(line, @"^mouse\s*\{$", RegexOptions.CultureInvariant))
            {
                if (foundStart >= 0)
                    throw Error(path, index + 1, "only one mouse block is allowed");

                foundStart = index;
                var blockDepth = BraceDelta(line);
                var cursor = index + 1;
                while (cursor < lines.Length && blockDepth > 0)
                {
                    blockDepth += BraceDelta(StripComment(lines[cursor]));
                    cursor++;
                }
                if (blockDepth != 0)
                    throw Error(path, index + 1, "unclosed mouse block");

                foundEnd = cursor;
                index = cursor - 1;
                depth = 0;
                continue;
            }

            depth += BraceDelta(line);
            if (depth < 0)
                depth = 0;
        }

        if (foundStart < 0)
            return new MouseDslExtraction(text, MouseMotionProfile.Default);

        var profile = ParseMouse(lines, foundStart, foundEnd, path);
        for (var index = foundStart; index < foundEnd; index++)
            lines[index] = string.Empty;
        return new MouseDslExtraction(string.Join('\n', lines), profile);
    }

    private static MouseMotionProfile ParseMouse(string[] lines, int start, int end, string path)
    {
        var defaults = MouseMotionProfile.Default;
        var engine = defaults.Engine;
        var updateMs = defaults.UpdateIntervalMs;
        var pressMs = defaults.PressMs;
        var releaseMs = defaults.ReleaseMs;
        var curve = defaults.Curve;
        var normal = defaults.NormalSpeed;
        var precision = defaults.PrecisionSpeed;
        var fine = defaults.FineSpeed;
        var fast = defaults.FastSpeed;
        var socd = defaults.Socd;
        var tapNudge = defaults.TapNudgePixels;
        var maxCatchup = defaults.MaxCatchupMs;
        string? section = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = start + 1; index < end - 1; index++)
        {
            var lineNumber = index + 1;
            var line = StripComment(lines[index].TrimEnd('\r')).Trim();
            if (line.Length == 0)
                continue;

            var subsection = Regex.Match(line, @"^(response|speed)\s*\{$", RegexOptions.CultureInvariant);
            if (subsection.Success)
            {
                if (section is not null)
                    throw Error(path, lineNumber, "mouse blocks may not be nested beyond response/speed");
                section = subsection.Groups[1].Value;
                continue;
            }

            if (line == "}")
            {
                if (section is null)
                    throw Error(path, lineNumber, "unexpected '}' inside mouse block");
                section = null;
                continue;
            }

            var settingMatch = Regex.Match(
                line,
                @"^([A-Za-z0-9_]+)\s*=\s*(.+)$",
                RegexOptions.CultureInvariant);
            if (!settingMatch.Success)
                throw Error(path, lineNumber, $"unknown mouse setting: {line}");

            var setting = settingMatch.Groups[1].Value;
            var raw = settingMatch.Groups[2].Value;
            var location = section is null ? setting : $"{section}.{setting}";
            if (!seen.Add(location))
                throw Error(path, lineNumber, $"duplicate mouse setting '{location}'");

            if (section == "response")
            {
                switch (setting)
                {
                    case "press":
                        pressMs = ParseDuration(path, lineNumber, raw, "response.press", positive: false);
                        break;
                    case "release":
                        releaseMs = ParseDuration(path, lineNumber, raw, "response.release", positive: false);
                        break;
                    case "curve":
                        curve = ParseToken(path, lineNumber, raw, "response.curve");
                        if (curve is not ("linear" or "smoothstep"))
                            throw Error(path, lineNumber, "mouse.response.curve supports 'linear' or 'smoothstep'");
                        break;
                    default:
                        throw Error(path, lineNumber, $"unknown mouse.response setting '{setting}'");
                }
                continue;
            }

            if (section == "speed")
            {
                var value = ParseSpeed(path, lineNumber, raw, setting);
                switch (setting)
                {
                    case "normal": normal = value; break;
                    case "precision": precision = value; break;
                    case "fine": fine = value; break;
                    case "fast": fast = value; break;
                    default: throw Error(path, lineNumber, $"unknown mouse.speed setting '{setting}'");
                }
                continue;
            }

            if (section is not null)
                throw Error(path, lineNumber, $"unknown mouse subsection '{section}'");

            switch (setting)
            {
                case "engine":
                    engine = ParseToken(path, lineNumber, raw, "engine");
                    if (engine != "virtual_stick")
                        throw Error(path, lineNumber, "mouse.engine currently supports only 'virtual_stick'");
                    break;
                case "update":
                    updateMs = ParseDuration(path, lineNumber, raw, "update", positive: true);
                    break;
                case "socd":
                    socd = ParseToken(path, lineNumber, raw, "socd");
                    if (socd != "neutral")
                        throw Error(path, lineNumber, "mouse.socd currently supports only 'neutral'");
                    break;
                case "tap_nudge":
                    tapNudge = ParsePixels(path, lineNumber, raw, "tap_nudge");
                    break;
                case "max_catchup":
                    maxCatchup = ParseDuration(path, lineNumber, raw, "max_catchup", positive: true);
                    break;
                default:
                    throw Error(path, lineNumber, $"unknown mouse setting '{setting}'");
            }
        }

        if (section is not null)
            throw Error(path, end, $"unclosed mouse.{section} block");

        return new MouseMotionProfile(
            engine,
            updateMs,
            pressMs,
            releaseMs,
            curve,
            normal,
            precision,
            fine,
            fast,
            socd,
            tapNudge,
            maxCatchup);
    }

    private static int ParseDuration(
        string path,
        int lineNumber,
        string raw,
        string setting,
        bool positive)
    {
        var token = TrimTerminator(raw);
        var match = Regex.Match(token, @"^(\d+)\s*ms$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            throw Error(path, lineNumber, $"mouse.{setting} must be a duration such as 8ms");
        var value = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        if (positive && value <= 0)
            throw Error(path, lineNumber, $"mouse.{setting} must be greater than 0ms");
        return value;
    }

    private static double ParseSpeed(string path, int lineNumber, string raw, string setting)
    {
        var token = TrimTerminator(raw);
        var match = Regex.Match(
            token,
            @"^(\d+(?:\.\d+)?)\s*(?:px/s)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw Error(path, lineNumber, $"mouse.speed.{setting} must be a non-negative number or px/s value");
        return value;
    }

    private static int ParsePixels(string path, int lineNumber, string raw, string setting)
    {
        var token = TrimTerminator(raw);
        var match = Regex.Match(token, @"^(\d+)\s*px$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            throw Error(path, lineNumber, $"mouse.{setting} must be a pixel value such as 1px");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static string ParseToken(string path, int lineNumber, string raw, string setting)
    {
        var token = TrimTerminator(raw).ToLowerInvariant();
        if (!Regex.IsMatch(token, @"^[a-z0-9_-]+$", RegexOptions.CultureInvariant))
            throw Error(path, lineNumber, $"invalid mouse.{setting} value '{raw.Trim()}'");
        return token;
    }

    private static string TrimTerminator(string value)
        => value.Trim().TrimEnd(';').Trim();

    private static string StripComment(string line)
    {
        var inString = false;
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (character == '\\')
                    escaped = true;
                else if (character == '"')
                    inString = false;
            }
            else if (character == '"')
            {
                inString = true;
            }
            else if (character == '/' && index + 1 < line.Length && line[index + 1] == '/')
            {
                return line[..index];
            }
        }
        return line;
    }

    private static int BraceDelta(string line)
        => line.Count(character => character == '{') - line.Count(character => character == '}');

    private static InvalidDataException Error(string path, int line, string message)
        => new($"{path}:{line}: {message}");
}

internal readonly record struct MouseDslExtraction(string SourceWithoutMouse, MouseMotionProfile Profile);
