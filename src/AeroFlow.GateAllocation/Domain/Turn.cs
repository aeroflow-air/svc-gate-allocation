namespace AeroFlow.GateAllocation.Domain;

/// <summary>A flight's need for a gate: which flight, how big the aircraft is, and when it is on block.</summary>
public sealed record Turn(FlightNumber Flight, AircraftSize Aircraft, TimeWindow Window);

/// <summary>A <see cref="Turn"/> placed on a gate.</summary>
public sealed record Allocation(Turn Turn, GateId Gate)
{
    public FlightNumber Flight => Turn.Flight;
}
