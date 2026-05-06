using System.Globalization;
using OvcinaHra.Shared.Domain.Enums;
using OvcinaHra.Shared.Extensions;

namespace OvcinaHra.Api.Services;

public static class PragueTimeFormatter
{
    private const string PragueWindowsTimeZoneId = "Central European Standard Time";

    public static DateTime ToPragueTime(DateTime utcDateTime)
    {
        var utc = utcDateTime.Kind switch
        {
            DateTimeKind.Utc => utcDateTime,
            DateTimeKind.Local => utcDateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)
        };

        try
        {
            return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, PragueWindowsTimeZoneId);
        }
        catch (TimeZoneNotFoundException) when (TimeZoneInfo.TryConvertWindowsIdToIanaId(PragueWindowsTimeZoneId, out var ianaId))
        {
            return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, ianaId);
        }
        catch (InvalidTimeZoneException) when (TimeZoneInfo.TryConvertWindowsIdToIanaId(PragueWindowsTimeZoneId, out var ianaId))
        {
            return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, ianaId);
        }
    }

    public static string Format(DateTime utcDateTime, string format, IFormatProvider? provider = null) =>
        ToPragueTime(utcDateTime).ToString(format, provider);

    public static string FormatTimeSlotDisplay(
        GameTimePhase stage,
        int? inGameYear,
        DateTime startTimeUtc,
        decimal durationHours)
    {
        var yearPrefix = inGameYear is int year ? $"Rok {year}, " : "";
        var start = Format(startTimeUtc, "d.M. HH:mm", CultureInfo.InvariantCulture);
        return $"{stage.GetDisplayName()}: {yearPrefix}{start} ({durationHours:0.#} h)";
    }
}
