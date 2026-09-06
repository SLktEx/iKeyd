namespace iKeyd.Core.Configuration;

/// <summary>
/// Platform-neutral clipboard-history policy carried by both JSON and compiled profiles.
/// The system clipboard itself is not encrypted or replaced by these settings.
/// </summary>
public sealed record ClipboardHistoryProfile
{
    public static ClipboardHistoryProfile Default { get; } = new();

    public ClipboardHistoryProfile(
        bool history = true,
        int maxItems = 20,
        bool persist = true,
        bool images = true,
        string encryption = "user",
        string cipher = "auto",
        string? directory = null)
    {
        if (maxItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Clipboard maxItems must be greater than zero.");
        if (!string.Equals(encryption, "user", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Clipboard encryption currently supports only 'user'.", nameof(encryption));

        var normalizedCipher = NormalizeCipher(cipher);
        if (directory is not null && string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Clipboard directory must not be empty when specified.", nameof(directory));

        History = history;
        MaxItems = maxItems;
        Persist = persist;
        Images = images;
        Encryption = "user";
        Cipher = normalizedCipher;
        Directory = directory;
    }

    public bool History { get; }
    public int MaxItems { get; }
    public bool Persist { get; }
    public bool Images { get; }
    public string Encryption { get; }
    public string Cipher { get; }
    public string? Directory { get; }

    private static string NormalizeCipher(string cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            throw new ArgumentException("Clipboard cipher must not be empty.", nameof(cipher));

        return cipher.Trim().ToLowerInvariant() switch
        {
            "auto" => "auto",
            "chacha20-poly1305" or "chacha20_poly1305" => "chacha20-poly1305",
            _ => throw new ArgumentException(
                "Clipboard cipher currently supports 'auto' or 'chacha20-poly1305'.",
                nameof(cipher))
        };
    }
}
