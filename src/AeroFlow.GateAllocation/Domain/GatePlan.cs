using System.Collections.Immutable;

namespace AeroFlow.GateAllocation.Domain;

/// <summary>
/// Immutable picture of an airport's gates and who is on them. Create with <see cref="Create"/>;
/// change it only through <see cref="GatePlanner"/>, which returns a new plan.
/// </summary>
public sealed record GatePlan
{
    private GatePlan(ImmutableDictionary<GateId, Gate> gates, TimeSpan minimumTurnaround)
    {
        Gates = gates;
        MinimumTurnaround = minimumTurnaround;
    }

    public ImmutableDictionary<GateId, Gate> Gates { get; init; }

    public ImmutableDictionary<FlightNumber, Allocation> Allocations { get; init; } =
        ImmutableDictionary<FlightNumber, Allocation>.Empty;

    /// <summary>Clear time a gate needs between one aircraft leaving and the next arriving.</summary>
    public TimeSpan MinimumTurnaround { get; }

    public static Result<GatePlan> Create(IEnumerable<Gate> gates, TimeSpan minimumTurnaround)
    {
        if (minimumTurnaround < TimeSpan.Zero)
        {
            return new NegativeTurnaround(minimumTurnaround);
        }

        var list = gates.ToList();
        return list.GroupBy(g => g.Id).FirstOrNone(group => group.Count() > 1).Match<Result<GatePlan>>(
            duplicate => new DuplicateGate(duplicate.Key),
            () => new GatePlan(list.ToImmutableDictionary(g => g.Id), minimumTurnaround));
    }

    public Option<Gate> FindGate(GateId id) => Gates.Find(id);

    public Option<Allocation> AllocationFor(FlightNumber flight) => Allocations.Find(flight);

    /// <summary>Allocations on <paramref name="gate"/>, earliest first.</summary>
    public ImmutableList<Allocation> AllocationsAt(GateId gate) =>
        Allocations.Values
            .Where(a => a.Gate == gate)
            .OrderBy(a => a.Turn.Window.Start)
            .ThenBy(a => a.Flight.Value, StringComparer.Ordinal)
            .ToImmutableList();
}
