namespace SineusArenaVersus.Game;

/// <summary>
/// Sender attribution for an injected pack. Uses plain floats so match/net tests
/// do not need Unity assemblies at load time.
/// </summary>
public readonly record struct InjectMarkerInfo(string Label, float R, float G, float B, float A = 1f);
