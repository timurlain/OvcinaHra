namespace OvcinaHra.Client.Components;

public record OvcinaMapClickEventArgs(double Latitude, double Longitude);

public record OvcinaMarkerDragEventArgs(int Id, double Latitude, double Longitude, double OrigLatitude, double OrigLongitude);

public record PiePayloadEventArgs(int LocationId, string Payload);
