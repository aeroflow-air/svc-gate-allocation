using System.Diagnostics;

namespace AeroFlow.GateAllocation.Domain;

/// <summary>Pure gate allocation rules. No clock, no storage, no exceptions for business failures.</summary>
public static class GatePlanner
{
    public static Result<GatePlan> Apply(GatePlan plan, GateCommand command) => command switch
    {
        AllocateGate c => Allocate(plan, c),
        ReassignGate c => Reassign(plan, c),
        RetimeTurn c => Retime(plan, c),
        ReleaseGate c => Release(plan, c),
        CloseGate c => Close(plan, c),
        ReopenGate c => Reopen(plan, c),
        _ => throw new UnreachableException($"Unhandled gate command {command.Name}."),
    };

    /// <summary>
    /// Best gate for <paramref name="turn"/>, or <c>None</c> if nothing fits. "Best" is the smallest gate that
    /// takes the aircraft (keeping big gates free for big aircraft), then gate id for a stable answer.
    /// </summary>
    public static Option<GateId> Suggest(GatePlan plan, Turn turn) =>
        plan.Gates.Values
            .Where(gate => Check(plan, gate, turn) is Result<Gate>.Ok)
            .OrderBy(gate => gate.MaxSize)
            .ThenBy(gate => gate.Id.Value, StringComparer.Ordinal)
            .Select(gate => gate.Id)
            .FirstOrNone();

    private static Result<GatePlan> Allocate(GatePlan plan, AllocateGate c) =>
        plan.AllocationFor(c.Turn.Flight).Match(
            existing => new AlreadyAllocated(existing.Flight, existing.Gate),
            () => c.Gate
                .Match(
                    some: gate => (Result<GateId>)gate,
                    none: () => Suggest(plan, c.Turn).ToResult(() => new NoGateAvailable(c.Turn.Flight)))
                .Bind(gate => Place(plan, c.Turn, gate)));

    private static Result<GatePlan> Reassign(GatePlan plan, ReassignGate c) =>
        Existing(plan, c.Flight).Bind(current => current.Gate == c.Gate
            ? new AlreadyAtGate(c.Flight, c.Gate)
            : Place(plan, current.Turn, c.Gate));

    private static Result<GatePlan> Retime(GatePlan plan, RetimeTurn c) =>
        Existing(plan, c.Flight).Bind(current => Place(plan, current.Turn with { Window = c.Window }, current.Gate));

    private static Result<GatePlan> Release(GatePlan plan, ReleaseGate c) =>
        Existing(plan, c.Flight).Map(current => plan with { Allocations = plan.Allocations.Remove(current.Flight) });

    private static Result<GatePlan> Close(GatePlan plan, CloseGate c) =>
        Gate(plan, c.Gate).Bind<GatePlan>(gate => plan.AllocationsAt(gate.Id) switch
        {
            { Count: > 0 } allocations => new GateHasAllocations(gate.Id, allocations.Count),
            _ => WithGate(plan, gate with { Status = new GateStatus.Closed(c.Reason) }),
        });

    private static Result<GatePlan> Reopen(GatePlan plan, ReopenGate c) =>
        Gate(plan, c.Gate).Bind<GatePlan>(gate => gate.Status switch
        {
            GateStatus.Closed => WithGate(plan, gate with { Status = new GateStatus.Open() }),
            _ => new GateNotClosed(gate.Id),
        });

    /// <summary>Puts (or moves) <paramref name="turn"/> onto <paramref name="gateId"/> if every rule allows it.</summary>
    private static Result<GatePlan> Place(GatePlan plan, Turn turn, GateId gateId) =>
        Gate(plan, gateId)
            .Bind(gate => Check(plan, gate, turn))
            .Map(gate => plan with { Allocations = plan.Allocations.SetItem(turn.Flight, new Allocation(turn, gate.Id)) });

    /// <summary>
    /// The gate rules for one turn: the gate is open, the aircraft fits, and no other flight is on the gate
    /// within the turnaround buffer. The turn's own current allocation is ignored so moves and retimes work.
    /// </summary>
    private static Result<Gate> Check(GatePlan plan, Gate gate, Turn turn)
    {
        if (gate.Status is GateStatus.Closed closed)
        {
            return new GateClosed(gate.Id, closed.Reason);
        }

        if (!gate.Accepts(turn.Aircraft))
        {
            return new AircraftTooLarge(gate.Id, gate.MaxSize, turn.Aircraft);
        }

        return plan.AllocationsAt(gate.Id)
            .FirstOrNone(other => other.Flight != turn.Flight && other.Turn.Window.Overlaps(turn.Window, plan.MinimumTurnaround))
            .Match<Result<Gate>>(
                clash => new GateConflict(gate.Id, clash.Flight, clash.Turn.Window, plan.MinimumTurnaround),
                () => gate);
    }

    private static Result<Gate> Gate(GatePlan plan, GateId id) =>
        plan.FindGate(id).ToResult(() => new GateNotFound(id));

    private static Result<Allocation> Existing(GatePlan plan, FlightNumber flight) =>
        plan.AllocationFor(flight).ToResult(() => new AllocationNotFound(flight));

    private static GatePlan WithGate(GatePlan plan, Gate gate) =>
        plan with { Gates = plan.Gates.SetItem(gate.Id, gate) };
}
