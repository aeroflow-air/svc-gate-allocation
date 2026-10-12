namespace AeroFlow.GateAllocation.Messaging;

/// <summary>Binds the <c>FlightEvents</c> configuration section.</summary>
public sealed class FlightEventsOptions
{
    public const string SectionName = "FlightEvents";

    /// <summary>Aspire connection name; the processor only starts when <c>ConnectionStrings:messaging</c> is set.</summary>
    public const string ConnectionName = "messaging";

    public string Topic { get; set; } = "flight-events";

    public string Subscription { get; set; } = "gate-allocation";

    /// <summary>The airport whose gates this service plans. Matches the simulator's home airport.</summary>
    public string HomeAirport { get; set; } = "LHR";

    /// <summary>How long before scheduled departure a departing flight takes its gate.</summary>
    public int GateMinutesBeforeDeparture { get; set; } = 45;

    /// <summary>The simulator does not send aircraft type, so every turn is treated as this size.</summary>
    public AeroFlow.GateAllocation.Domain.AircraftSize DefaultAircraftSize { get; set; } = AeroFlow.GateAllocation.Domain.AircraftSize.C;
}
