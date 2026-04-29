using System.Reflection;

namespace OvcinaHra.Client.Services;

/// <summary>
/// Identity of the WebAssembly bundle currently executing in the browser.
/// Production Docker builds stamp <c>SourceRevisionId</c> into
/// <see cref="AssemblyInformationalVersionAttribute"/> so boot-time version
/// checks can compare the loaded client against the deployed API commit.
/// </summary>
public static class ClientBuildInfo
{
    private const string UnknownCommit = "unknown";

    private static readonly string InformationalVersion =
        typeof(ClientBuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "dev";

    public static string DisplayVersion => InformationalVersion.Split('+')[0];

    public static string RawInformationalVersion => InformationalVersion;

    public static string? Commit
    {
        get
        {
            var separator = InformationalVersion.IndexOf('+', StringComparison.Ordinal);
            if (separator < 0 || separator == InformationalVersion.Length - 1)
                return null;

            var commit = InformationalVersion[(separator + 1)..].Trim();
            return string.IsNullOrWhiteSpace(commit)
                || string.Equals(commit, UnknownCommit, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : commit;
        }
    }
}
