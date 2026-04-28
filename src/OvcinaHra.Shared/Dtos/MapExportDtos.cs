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
