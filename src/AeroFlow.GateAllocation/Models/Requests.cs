using AeroFlow.GateAllocation.Domain;

namespace AeroFlow.GateAllocation.Models;

/// <summary>
/// Body for <c>POST api/allocations</c>. Omit <see cref="Gate"/> to let the planner pick one; the nullable
/// field becomes an <see cref="Option{T}"/> here and never reaches the domain as <c>null</c>.
/// </summary>
public sealed record AllocationRequest
{
    public string? FlightNumber { get; init; }
    public string? AircraftSize { get; init; }
    public DateTimeOffset? OnBlock { get; init; }
    public DateTimeOffset? OffBlock { get; init; }
    public string? Gate { get; init; }

    public Result<AllocateGate> ToCommand() =>
        Domain.FlightNumber.Parse(FlightNumber).Bind(flight =>
        AircraftSizes.Parse(AircraftSize).Bind(size =>
        Fields.Window(OnBlock, OffBlock).Bind(window =>
        Fields.OptionalGate(Gate).Map(gate =>
            new AllocateGate(new Turn(flight, size, window), gate)))));
}

/// <summary>Body for <c>PUT api/allocations/{flightNumber}/gate</c>.</summary>
public sealed record ReassignRequest
{
    public string? Gate { get; init; }
}

/// <summary>Body for <c>PUT api/allocations/{flightNumber}/window</c>.</summary>
public sealed record RetimeRequest
{
    public DateTimeOffset? OnBlock { get; init; }
    public DateTimeOffset? OffBlock { get; init; }
}

/// <summary>Body for <c>POST api/gates/{gate}/close</c>.</summary>
public sealed record CloseGateRequest
{
    public string? Reason { get; init; }
}

/// <summary>Edge helpers that turn loose JSON fields into domain values.</summary>
public static class Fields
{
    public static Result<TimeWindow> Window(DateTimeOffset? onBlock, DateTimeOffset? offBlock) =>
        Require(onBlock, "onBlock").Bind(start =>
        Require(offBlock, "offBlock").Bind(end =>
            TimeWindow.Create(start, end)));

    public static Result<Option<GateId>> OptionalGate(string? gate) =>
        string.IsNullOrWhiteSpace(gate)
            ? Option.None<GateId>()
            : GateId.Parse(gate).Map(Option.Some);

    public static Result<string> RequireText(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? new InvalidRequest($"'{field}' is required.") : value.Trim();

    private static Result<DateTimeOffset> Require(DateTimeOffset? value, string field) =>
        value is { } v ? v : new InvalidRequest($"'{field}' is required.");
}

/// <summary>Malformed request body (maps to 400).</summary>
public sealed record InvalidRequest(string Message) : ValidationError("invalid_request", Message);
