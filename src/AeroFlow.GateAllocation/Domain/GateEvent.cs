using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroFlow.GateAllocation.Domain;

/// <summary>Event types published on the <c>gate-events</c> topic (owned by this service, ADR-0013).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GateEventType>))]
public enum GateEventType
{
    GateAssigned,
    GateChanged,
    GateReleased,
}

/// <summary>
/// Wire contract for a gate event (JSON body of a Service Bus message). <c>Subject</c> carries the event type and
/// <c>CorrelationId</c> the flight id from the flight event that caused it (falling back to the flight number).
/// </summary>
public sealed record GateEvent(
    Guid EventId,
    GateEventType EventType,
    string FlightNumber,
    string? FlightId,
    string Gate,
    DateTimeOffset OccurredAt,
    string? PreviousGate = null,
    DateTimeOffset? OnBlock = null,
    DateTimeOffset? OffBlock = null)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Pure: works out which gate events a change to one flight's allocation produced.</summary>
public static class GateEvents
{
    public static ImmutableList<GateEvent> Diff(
        GatePlan before, GatePlan after, FlightNumber flight, string? flightId, DateTimeOffset at, Func<Guid> newId)
    {
        var was = before.AllocationFor(flight);
        var now = after.AllocationFor(flight);
        return (was, now) switch
        {
            (Option<Allocation>.None, Option<Allocation>.Some n) =>
                [Make(GateEventType.GateAssigned, n.Value, null)],
            // A new gate or a new window (a delay) both count as a change; PreviousGate is set only when the gate moved.
            (Option<Allocation>.Some w, Option<Allocation>.Some n) when w.Value != n.Value =>
                [Make(GateEventType.GateChanged, n.Value, w.Value.Gate != n.Value.Gate ? w.Value.Gate.Value : null)],
            (Option<Allocation>.Some w, Option<Allocation>.None) =>
                [Make(GateEventType.GateReleased, w.Value, null) with { OnBlock = null, OffBlock = null }],
            _ => [],
        };

        GateEvent Make(GateEventType type, Allocation a, string? previous) =>
            new(newId(), type, flight.Value, flightId, a.Gate.Value, at, previous, a.Turn.Window.Start, a.Turn.Window.End);
    }
}
