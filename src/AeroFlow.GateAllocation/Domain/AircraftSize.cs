namespace AeroFlow.GateAllocation.Domain;

/// <summary>
/// ICAO aerodrome reference code (wingspan category), smallest to largest. The order is meaningful:
/// a gate rated for a size takes that size and anything smaller.
/// </summary>
public enum AircraftSize
{
    /// <summary>Up to 36 m wingspan, e.g. A320, 737.</summary>
    C,

    /// <summary>Up to 52 m, e.g. 757, 767.</summary>
    D,

    /// <summary>Up to 65 m, e.g. 777, 787, A350.</summary>
    E,

    /// <summary>Up to 80 m, e.g. A380, 747-8.</summary>
    F,
}

public static class AircraftSizes
{
    /// <summary>Accepts a single letter C-F in either case; enum names and numbers are not accepted.</summary>
    public static Result<AircraftSize> Parse(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "C" => AircraftSize.C,
            "D" => AircraftSize.D,
            "E" => AircraftSize.E,
            "F" => AircraftSize.F,
            _ => new InvalidAircraftSize(value),
        };
}
