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
public sealed class GateEventHandler(IGatePlanStore store, IOptions<FlightEventsOptions> options)
{
    private readonly FlightEventsOptions _options = options.Value;

    public HandleOutcome Handle(FlightEventMessage evt)
    {
        if (!string.Equals(evt.Origin, _options.HomeAirport, StringComparison.OrdinalIgnoreCase))
        {
            return new HandleOutcome.Ignored($"not departing {_options.HomeAirport}");
        }

        var command = FlightNumber.Parse(evt.FlightNumber).Bind(flight => ToCommand(flight, evt));
        return command switch
        {
            Result<Option<GateCommand>>.Failure f => new HandleOutcome.Rejected(f.Error),
            Result<Option<GateCommand>>.Ok { Value: Option<GateCommand>.Some some } => Apply(some.Value),
            _ => new HandleOutcome.Ignored($"{evt.EventType} does not change the gate plan"),
        };
    }

    private HandleOutcome Apply(GateCommand command) =>
        store.Update(plan => GatePlanner.Apply(plan, command)).Match<HandleOutcome>(
            _ => new HandleOutcome.Applied(command.Name),
            error => new HandleOutcome.Rejected(error));

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
