namespace TideReader.Backend.Services;

public sealed record OverlayDependencyObservation(string State, DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt, string ErrorCode);
public interface IOverlayDiagnostics { OverlayDependencyObservation ObserveOverlay(); }
