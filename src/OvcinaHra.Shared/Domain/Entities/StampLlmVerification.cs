namespace OvcinaHra.Shared.Domain.Entities;

public class StampLlmVerification
{
    /// <summary>
    /// "verify" — 1-to-1 LLM check against a chosen Location reference (existing /verify-llm flow).
    /// "recognize" — 1-to-N rank against all stamped Locations in a game (new /recognize flow).
    /// </summary>
    public const string ModeVerify = "verify";

    /// <summary>
    /// Recognize-stash audit row. <see cref="LocationId"/> stores the top-1 candidate (or 0 when none).
    /// </summary>
    public const string ModeRecognize = "recognize";

    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public required string OrganizerUserId { get; set; }
    public required string OrganizerName { get; set; }

    /// <summary>
    /// For <see cref="ModeVerify"/> — the Location whose stamp was checked.
    /// For <see cref="ModeRecognize"/> — the top-1 Location candidate.
    /// </summary>
    public int LocationId { get; set; }
    public int? ContextStashId { get; set; }
    public int? ContextQuestId { get; set; }
    public bool Match { get; set; }
    public double Confidence { get; set; }
    public int LatencyMs { get; set; }
    public required string RawResponse { get; set; }

    /// <summary>
    /// Discriminator between the verify (1-to-1) and recognize (1-to-N) audit rows.
    /// Defaults to <see cref="ModeVerify"/> for back-compat with existing rows.
    /// </summary>
    public string Mode { get; set; } = ModeVerify;

    /// <summary>
    /// Recognize-only: the Game whose stamped locations were the candidate set.
    /// Null for verify rows.
    /// </summary>
    public int? GameId { get; set; }

    /// <summary>
    /// Recognize-only: how many reference stamps were sent to the LLM across all batches.
    /// Null for verify rows.
    /// </summary>
    public int? ReferencesScanned { get; set; }

    public Location Location { get; set; } = null!;
}
