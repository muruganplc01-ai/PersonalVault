namespace PersonalVault.Storage;

/// <summary>
/// One-time setup for the "Share Account" feature - see README.md's "Sharing an
/// account (one-time link)" section. ViewerBaseUrl needs to be edited once you've
/// published docs/share/index.html via GitHub Pages (Settings -> Pages -> Deploy from
/// branch -> main -> /docs), the same "edit this one thing yourself" pattern already
/// used for credentials.json (see Utils/CredentialBootstrap.cs and README.md).
/// </summary>
public static class ShareConfig
{
    /// <summary>
    /// Replace with your own GitHub Pages URL, including the trailing slash, e.g.
    /// "https://yourusername.github.io/PersonalVault/share/". Share links are built by
    /// appending "#id=...&amp;key=...&amp;exp=..." to this.
    /// </summary>
    public const string ViewerBaseUrl = "https://YOUR-GITHUB-USERNAME.github.io/PersonalVault/share/";
}
