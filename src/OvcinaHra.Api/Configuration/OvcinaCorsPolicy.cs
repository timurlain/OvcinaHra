using System.Collections.Immutable;

namespace OvcinaHra.Api.Configuration;

/// <summary>
/// Builds the effective CORS allowlist for the Ovčina API. Always unions a
/// hard-coded set of well-known ecosystem production hostnames on top of
/// whatever <c>Cors:Origins</c> configuration provides, so a misconfigured
/// env-var deploy can never silently lock out an ecosystem client.
/// </summary>
/// <remarks>
/// Localhost origins are NOT in this list — they are matched via
/// <c>Uri.IsLoopback</c> at runtime so any port works without enumeration.
/// Add a hostname here only when it is part of the permanent Ovčina
/// production ecosystem; ephemeral preview hostnames belong in config.
///
/// History: Issue #244 (2026-04-27) — the prod Container App env vars
/// <c>Cors__Origins__N</c> only listed <c>https://hra.ovcina.cz</c> and the
/// SWA fallback; Glejt PWA at <c>https://glejt.ovcina.cz</c> was blocked.
/// PR #286 added Glejt to a code-level fallback array, but the config branch
/// fully replaced that fallback when env vars were set, so the code change
/// was silently inert in prod. This helper closes that footgun.
/// </remarks>
public static class OvcinaCorsPolicy
{
    /// <summary>
    /// Production hostnames for the Ovčina ecosystem that must always be
    /// CORS-allowed, regardless of <c>Cors:Origins</c> configuration.
    /// </summary>
    /// <remarks>
    /// Exposed as <see cref="ImmutableArray{T}"/> so callers cannot mutate
    /// the underlying storage at runtime and accidentally remove an
    /// ecosystem origin from the allowlist.
    /// </remarks>
    public static readonly ImmutableArray<string> EcosystemOrigins =
    [
        "https://hra.ovcina.cz",
        "https://glejt.ovcina.cz",
    ];

    /// <summary>
    /// Returns the union of <see cref="EcosystemOrigins"/> and
    /// <paramref name="configuredOrigins"/> (case-insensitive de-dupe).
    /// Configured origins can ADD to the allowlist but never remove an
    /// ecosystem entry. Each configured origin is trimmed before insertion
    /// so a stray space in an env var (e.g. <c>Cors__Origins__0=" https://x"</c>)
    /// won't silently desync from the browser <c>Origin</c> header.
    /// </summary>
    /// <returns>
    /// An <see cref="IReadOnlySet{T}"/> backed by a case-insensitive
    /// <see cref="HashSet{T}"/> for O(1) per-request membership checks.
    /// </returns>
    public static IReadOnlySet<string> BuildEffectiveOrigins(IEnumerable<string>? configuredOrigins)
    {
        var set = new HashSet<string>(EcosystemOrigins, StringComparer.OrdinalIgnoreCase);
        if (configuredOrigins is not null)
        {
            foreach (var origin in configuredOrigins)
            {
                var trimmed = origin?.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    set.Add(trimmed);
                }
            }
        }
        return set;
    }
}
