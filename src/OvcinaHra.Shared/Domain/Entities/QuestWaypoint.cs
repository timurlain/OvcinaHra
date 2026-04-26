namespace OvcinaHra.Shared.Domain.Entities;

/// <summary>
/// Issue #214 — ordered location waypoints inside a quest.
///
/// The Map page (#207) wants to draw an animated path connecting a quest's
/// encounters in narrative order — Start → Setkání 1 → 2 → … → Finále.
/// The pre-existing schema couldn't support that:
/// <list type="bullet">
///   <item><see cref="QuestEncounter"/> is monster-fight metadata
///         (QuestId, MonsterId, Quantity) with no location anchor and
///         no order.</item>
///   <item><see cref="QuestLocationLink"/> ties quest↔location but has
///         no ordering field.</item>
/// </list>
/// QuestWaypoint adds the missing primitive: one row per stop, with
/// <see cref="Order"/> defining sequence and an optional Czech
/// <see cref="Label"/> per stop ("Začátek", "Brod", "Finále" …).
/// </summary>
public class QuestWaypoint
{
    public int Id { get; set; }
    public int QuestId { get; set; }
    public int LocationId { get; set; }

    /// <summary>1-based step number within the quest. Composite unique
    /// (QuestId, Order) enforces no two waypoints share the same step.</summary>
    public int Order { get; set; }

    /// <summary>Optional Czech caption shown on the map pin badge and in
    /// the editor row. Null = render the location's name as the label.</summary>
    public string? Label { get; set; }

    public Quest Quest { get; set; } = null!;
    public Location Location { get; set; } = null!;
}
