using System.Text.Json.Serialization;

namespace OvcinaHra.Shared.Dtos;

/// <summary>
/// Vector overlay drawn on top of a game's map — issue #96. Persisted as a
/// JSON string on <c>Game.OverlayJson</c>. Phase 1+2 ships 6 primitives
/// (text, freehand, polyline, rectangle, circle, polygon); icons + arrows
/// are deferred to Phase 3.
/// </summary>
public record MapOverlayDto(List<MapOverlayShape> Shapes);

/// <summary>
/// Latitude/longitude pair, always in that order, WGS84 decimal degrees.
/// </summary>
public record OverlayCoord(double Lat, double Lng);

/// <summary>
/// Discriminated union over shape variants. The <c>type</c> property in the
/// JSON payload drives <see cref="System.Text.Json"/> polymorphism. Shape
/// <c>Id</c> is a client-generated string (GUID) so the client can update
/// or delete a shape without a round-trip.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextShape),      "text")]
[JsonDerivedType(typeof(FreehandShape),  "freehand")]
[JsonDerivedType(typeof(PolylineShape),  "polyline")]
[JsonDerivedType(typeof(RectangleShape), "rectangle")]
[JsonDerivedType(typeof(CircleShape),    "circle")]
[JsonDerivedType(typeof(PolygonShape),   "polygon")]
[JsonDerivedType(typeof(IconShape),      "icon")]
public abstract record MapOverlayShape(string Id, string Color);

public record TextShape(
    string Id,
    string Color,
    OverlayCoord Coord,
    string Text,
    int FontSize = 14)
    : MapOverlayShape(Id, Color);

public record FreehandShape(
    string Id,
    string Color,
    double StrokeWidth,
    List<OverlayCoord> Points)
    : MapOverlayShape(Id, Color);

public record PolylineShape(
    string Id,
    string Color,
    double StrokeWidth,
    List<OverlayCoord> Points)
    : MapOverlayShape(Id, Color);

public record RectangleShape(
    string Id,
    string Color,
    string? FillColor,
    double StrokeWidth,
    OverlayCoord Sw,
    OverlayCoord Ne)
    : MapOverlayShape(Id, Color);

public record CircleShape(
    string Id,
    string Color,
    string? FillColor,
    double StrokeWidth,
    OverlayCoord Center,
    double RadiusMeters)
    : MapOverlayShape(Id, Color);

public record PolygonShape(
    string Id,
    string Color,
    string? FillColor,
    double StrokeWidth,
    List<OverlayCoord> Points)
    : MapOverlayShape(Id, Color);

/// <summary>
/// Curated-asset icon dropped at a point. <see cref="AssetKey"/> is one of
/// the slugs in <c>wwwroot/img/overlay-icons/</c> (flag, tent, chest, skull,
/// door, fire). <see cref="Color"/> tints the SVG via <c>currentColor</c>;
/// rotation is degrees clockwise from north, scale multiplies the base
/// 32-px viewBox. #96 Phase 3.
/// </summary>
public record IconShape(
    string Id,
    string Color,
    string AssetKey,
    OverlayCoord Coord,
    double Rotation = 0,
    double Scale = 1.0)
    : MapOverlayShape(Id, Color);
