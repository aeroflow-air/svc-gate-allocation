using AeroFlow.GateAllocation.Domain;

namespace AeroFlow.GateAllocation.Models;

/// <summary>API shape for an allocation. Kept separate from the domain <see cref="Allocation"/> so either can evolve.</summary>
/// <param name="FlightNumber">Flight number.</param>
/// <param name="Gate">Allocated gate id.</param>
/// <param name="AircraftSize">Aircraft size category, for example <c>Narrowbody</c>.</param>
/// <param name="OnBlock">When the aircraft arrives at the gate.</param>
/// <param name="OffBlock">When the aircraft leaves the gate.</param>
public sealed record AllocationResponse(
    string FlightNumber,
    string Gate,
    string AircraftSize,
    DateTimeOffset OnBlock,
    DateTimeOffset OffBlock)
{
    public static AllocationResponse From(Allocation allocation) => new(
        FlightNumber: allocation.Flight.Value,
        Gate: allocation.Gate.Value,
        AircraftSize: allocation.Turn.Aircraft.ToString(),
        OnBlock: allocation.Turn.Window.Start,
        OffBlock: allocation.Turn.Window.End);
}
