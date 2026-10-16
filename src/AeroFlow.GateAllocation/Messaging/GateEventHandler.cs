using AeroFlow.GateAllocation.Domain;
using AeroFlow.GateAllocation.Storage;
using Microsoft.Extensions.Options;

namespace AeroFlow.GateAllocation.Messaging;

/// <summary>What happened to a flight event. <see cref="Rejected"/> carries the domain error.</summary>
public abstract record HandleOutcome
{
    private HandleOutcome()
    {
    }

    public sealed record Applied(string Command) : HandleOutcome;

    /// <summary>Nothing to do here, for example an arrival or a boarding update.</summary>
    public sealed record Ignored(string Reason) : HandleOutcome;

    public sealed record Rejected(Error Error) : HandleOutcome;
}

/// <summary>
/// Turns simulator flight events into gate commands for departures from the home airport:
/// <c>Scheduled</c> allocates a gate (the planner picks one), <c>Delayed</c> retimes the turn,
/// <c>Departed</c> and <c>Cancelled</c> release the gate. Arrivals are ignored for now.
/// </summary>
public sealed class GateEventHandler(IGatePlanStore store, IOptions<FlightEventsOptions> options, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly FlightEventsOptions _options = options.Value;

    public HandleOutcome Handle(FlightEventMessage evt) => Handle(evt, out _);

    /// <summary>As <see cref="Handle(FlightEventMessage)"/>, also returning the gate events the change produced (empty unless applied).</summary>
    public HandleOutcome Handle(FlightEventMessage evt, out IReadOnlyList<GateEvent> gateEvents)
    {
        gateEvents = [];
        if (!string.Equals(evt.Origin, _options.HomeAirport, StringComparison.OrdinalIgnoreCase))
        {
            return new HandleOutcome.Ignored($"not departing {_options.HomeAirport}");
        }

        // Gate event ids are derived from the event id and correlated by the flight id, so both must be present:
        // a missing eventId (Guid.Empty) would give every such flight the same MessageId.
        if (evt.EventId == Guid.Empty)
        {
            return new HandleOutcome.Rejected(new MissingEventIdentity("eventId"));
        }

        if (string.IsNullOrWhiteSpace(evt.FlightId))
        {
            return new HandleOutcome.Rejected(new MissingEventIdentity("flightId"));
        }

        var command = FlightNumber.Parse(evt.FlightNumber).Bind(flight => ToCommand(flight, evt));
        return command switch
        {
            Result<Option<GateCommand>>.Failure f => new HandleOutcome.Rejected(f.Error),
            Result<Option<GateCommand>>.Ok { Value: Option<GateCommand>.Some some } => Apply(some.Value, evt, out gateEvents),
            _ => new HandleOutcome.Ignored($"{evt.EventType} does not change the gate plan"),
        };
    }

    /// <summary>
    /// Applies the command and, in the same atomic store update, queues the gate events it produced in the outbox
    /// (ADR-0017). The <see cref="OutboxDispatcher"/> sends them, so a failed send is retried rather than lost.
    /// </summary>
    private HandleOutcome Apply(GateCommand command, FlightEventMessage evt, out IReadOnlyList<GateEvent> gateEvents)
    {
        var at = _clock.GetUtcNow();
        var result = store.Update(
            plan => GatePlanner.Apply(plan, command),
            (before, after) => FlightOf(command) is { } flight
                ? GateEvents.Diff(before, after, flight, evt.FlightId, at, evt.EventId)
                : []);

        gateEvents = result.Match(r => r.Events, _ => []);
        return result.Match<HandleOutcome>(
            _ => new HandleOutcome.Applied(command.Name),
            error => new HandleOutcome.Rejected(error));
    }

    private static FlightNumber? FlightOf(GateCommand command) => command switch
    {
        AllocateGate a => a.Turn.Flight,
        ReassignGate r => r.Flight,
        RetimeTurn r => r.Flight,
        ReleaseGate r => r.Flight,
        _ => null,
    };

    private Result<Option<GateCommand>> ToCommand(FlightNumber flight, FlightEventMessage evt) => evt.EventType switch
    {
        FlightEventType.Scheduled => Window(evt.ScheduledDeparture).Map(window =>
            Some(new AllocateGate(new Turn(flight, _options.DefaultAircraftSize, window), Option.None<GateId>()))),
        FlightEventType.Delayed => Window(evt.ScheduledDeparture.AddMinutes(evt.DelayMinutes ?? 0)).Map(window =>
            Some(new RetimeTurn(flight, window))),
        FlightEventType.Departed or FlightEventType.Cancelled => Some(new ReleaseGate(flight)),
        _ => Option.None<GateCommand>(),
    };

    /// <summary>On block a set time before departure, off block at departure.</summary>
    private Result<TimeWindow> Window(DateTimeOffset departure) =>
        TimeWindow.Create(departure.AddMinutes(-_options.GateMinutesBeforeDeparture), departure);

    private static Option<GateCommand> Some(GateCommand command) => Option.Some(command);
}
