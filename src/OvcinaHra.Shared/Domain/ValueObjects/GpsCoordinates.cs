namespace OvcinaHra.Shared.Domain.ValueObjects;

public record GpsCoordinates
{
    public decimal Latitude { get; init; }
    public decimal Longitude { get; init; }

    public GpsCoordinates(decimal latitude, decimal longitude)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(latitude, -90m, nameof(latitude));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(latitude, 90m, nameof(latitude));
        ArgumentOutOfRangeException.ThrowIfLessThan(longitude, -180m, nameof(longitude));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(longitude, 180m, nameof(longitude));

        Latitude = latitude;
        Longitude = longitude;
    }

    // EF Core needs a parameterless constructor
    private GpsCoordinates() { }
}
