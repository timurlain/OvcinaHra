using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Shared.Extensions;

public static class TimeSlotDisplayExtensions
{
    public static string FormatTimeSlotDisplay(
        GameTimePhase stage,
        int? inGameYear,
        DateTime startTime,
        decimal durationHours)
    {
        var yearPrefix = inGameYear is int year ? $"Rok {year}, " : "";
        var localStartTime = startTime.ToLocalTime();
        return $"{stage.GetDisplayName()}: {yearPrefix}{localStartTime:d.M. HH:mm} ({durationHours:0.#} h)";
    }
}
