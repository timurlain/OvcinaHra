using System.Globalization;
using OvcinaHra.Api.Services;
using OvcinaHra.Shared.Domain.Enums;

namespace OvcinaHra.Api.Tests.Services;

public class PragueTimeFormatterTests
{
    [Fact]
    public void ToPragueTime_ConvertsUtcTimestampToCzechLocalTime()
    {
        var utc = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

        var local = PragueTimeFormatter.ToPragueTime(utc);

        Assert.Equal(new DateTime(2026, 5, 1, 10, 0, 0), local);
    }

    [Fact]
    public void FormatTimeSlotDisplay_UsesPragueTimeInServerGeneratedLabel()
    {
        var utc = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

        var label = PragueTimeFormatter.FormatTimeSlotDisplay(
            GameTimePhase.Start,
            2865,
            utc,
            2m);

        Assert.Equal("Start: Rok 2865, 1.5. 10:00 (2 h)", label);
    }

    [Fact]
    public void Format_UsesPragueTimeAndRequestedFormat()
    {
        var utc = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);

        var value = PragueTimeFormatter.Format(
            utc,
            "d.M.yyyy HH:mm",
            CultureInfo.InvariantCulture);

        Assert.Equal("1.5.2026 10:00", value);
    }
}
