using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Dtos;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MapExportBasemapStyle
{
    Tourist,
    Aerial,
    Basic,
    Osm,
    Blank
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MapExportKind
{
    Explorer,
    Organizer,
    Kingdom
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MapExportPageFormat
{
    A4Portrait,
    A3Portrait
}
