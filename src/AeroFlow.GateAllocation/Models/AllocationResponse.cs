using AeroFlow.GateAllocation.Domain;

namespace AeroFlow.GateAllocation.Models;

/// <summary>API shape for an allocation. Kept separate from the domain <see cref="Allocation"/> so either can evolve.</summary>
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
