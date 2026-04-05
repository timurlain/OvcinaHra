namespace OvcinaHra.Api.Tests.Domain;

public class GpsCoordinatesTests
{
    [Fact]
    public void GpsCoordinates_WithValidValues_CreatesSuccessfully()
    {
        var coords = new Shared.Domain.ValueObjects.GpsCoordinates(49.5m, 17.3m);
        Assert.Equal(49.5m, coords.Latitude);
        Assert.Equal(17.3m, coords.Longitude);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    public void GpsCoordinates_WithInvalidLatitude_ThrowsArgumentOutOfRange(decimal lat, decimal lon)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Shared.Domain.ValueObjects.GpsCoordinates(lat, lon));
    }

    [Theory]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void GpsCoordinates_WithInvalidLongitude_ThrowsArgumentOutOfRange(decimal lat, decimal lon)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Shared.Domain.ValueObjects.GpsCoordinates(lat, lon));
    }

    [Fact]
    public void GpsCoordinates_AtBoundaryValues_CreatesSuccessfully()
    {
        var north = new Shared.Domain.ValueObjects.GpsCoordinates(90m, 180m);
        var south = new Shared.Domain.ValueObjects.GpsCoordinates(-90m, -180m);
        Assert.Equal(90m, north.Latitude);
        Assert.Equal(-180m, south.Longitude);
    }
}
