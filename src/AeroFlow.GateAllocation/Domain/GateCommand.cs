namespace AeroFlow.GateAllocation.Domain;

/// <summary>Closed set of changes to a <see cref="GatePlan"/>. Cases are sealed; the base cannot be derived outside this assembly.</summary>
public abstract record GateCommand
{
    private protected GateCommand()
    {
    }

    public string Name => GetType().Name;
}

/// <summary>Put a turn on a gate. <c>None</c> for <paramref name="Gate"/> means "pick the best free gate".</summary>
public sealed record AllocateGate(Turn Turn, Option<GateId> Gate) : GateCommand;

public sealed record ReassignGate(FlightNumber Flight, GateId Gate) : GateCommand;

/// <summary>The flight's on-block window moved (a delay, say); it keeps its gate if the new window still fits.</summary>
public sealed record RetimeTurn(FlightNumber Flight, TimeWindow Window) : GateCommand;

public sealed record ReleaseGate(FlightNumber Flight) : GateCommand;

public sealed record CloseGate(GateId Gate, string Reason) : GateCommand;

public sealed record ReopenGate(GateId Gate) : GateCommand;
