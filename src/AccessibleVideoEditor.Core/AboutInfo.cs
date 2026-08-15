namespace AccessibleVideoEditor.Core;

/// <summary>
/// Who made this, what it is, and how to support it.
///
/// In Core rather than in the window so the CLI and the About dialog cannot
/// disagree about the version.
/// </summary>
public static class AboutInfo
{
    public const string Name = "Accessible Video Editor";

    public const string Version = "0.13.0";

    public const string Tagline =
        "A video editor where a blind editor is the primary user, not an afterthought.";

    public const string Author = "Cody Hurst";

    /// <summary>Cash App.</summary>
    public const string CashTag = "$churst90";

    /// <summary>
    /// Crypto addresses, to be filled in. Listed by name so the dialog shows
    /// what is coming rather than silently omitting it.
    /// </summary>
    public static readonly (string Coin, string Address)[] Crypto =
    [
        ("Bitcoin", ""),
        ("Ethereum", ""),
        ("Monero", ""),
    ];

    public static IEnumerable<(string Coin, string Address)> KnownCrypto =>
        Crypto.Where(c => c.Address.Length > 0);

    /// <summary>
    /// Read aloud by the About dialog. Written as sentences rather than as a
    /// layout, because it is going to be heard rather than looked at.
    /// </summary>
    public static string Speak()
    {
        var lines = new List<string>
        {
            $"{Name}, version {Version}.",
            Tagline,
            $"Made by {Author}.",
            $"Donations: Cash App {CashTag}.",
        };

        var crypto = KnownCrypto.ToList();

        lines.Add(crypto.Count == 0
            ? "Crypto addresses are not set yet."
            : $"Crypto: {string.Join(", ", crypto.Select(c => $"{c.Coin}, {c.Address}"))}.");

        return string.Join(" ", lines);
    }
}
