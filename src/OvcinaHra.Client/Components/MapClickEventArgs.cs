namespace OvcinaHra.Client.Components;

public record OvcinaMapClickEventArgs(double Latitude, double Longitude, bool CtrlKey = false);

public record OvcinaMarkerDragEventArgs(int Id, double Latitude, double Longitude, double OrigLatitude, double OrigLongitude);

public record PiePayloadEventArgs(int LocationId, string Payload);
