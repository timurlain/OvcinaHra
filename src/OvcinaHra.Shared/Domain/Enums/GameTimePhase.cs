using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Domain.Enums;

/// <summary>
/// Phase of a Game's timeline. Drives both <c>TreasureQuest.Difficulty</c>
/// (when treasure quests are appropriate to plant) and
/// <c>GameTimeSlot.Stage</c> (the colour-coded band on the Gantt agenda).
/// Renamed from <c>TreasureQuestDifficulty</c> in #182 — the previous name
/// undersold the second use case. Stored as a string column via
/// <c>HasConversion&lt;string&gt;()</c>, so the rename is wire-/data-compatible.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameTimePhase
{
    [Display(Name = "Start")]
    Start,

    [Display(Name = "Rozvoj hry")]
    Early,

    [Display(Name = "Střed hry")]
    Midgame,

    [Display(Name = "Závěr hry")]
    Lategame,

    /// <summary>Final wrap-up after the climax — debrief, awards, packing up. #182.</summary>
    [Display(Name = "Konec hry")]
    EndGame
}
