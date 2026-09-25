namespace AeroFlow.GateAllocation.Domain;

/// <summary>A gate (stand) at the airport: which aircraft fit it and whether it is in service.</summary>
public sealed record Gate(GateId Id, AircraftSize MaxSize, GateStatus Status)
{
    public bool Accepts(AircraftSize size) => size <= MaxSize;
}

/// <summary>Whether a gate can take allocations. Closed set: <see cref="Open"/> or <see cref="Closed"/> with a reason.</summary>
public abstract record GateStatus
{
    private GateStatus()
    {
    }

    public sealed record Open : GateStatus;

    public sealed record Closed(string Reason) : GateStatus;
}
