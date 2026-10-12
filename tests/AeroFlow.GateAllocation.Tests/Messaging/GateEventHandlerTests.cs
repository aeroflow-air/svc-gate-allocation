using AeroFlow.GateAllocation.Domain;
using AeroFlow.GateAllocation.Messaging;
using AeroFlow.GateAllocation.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroFlow.GateAllocation.Tests.Messaging;

public sealed class GateEventHandlerTests
{
    private static readonly DateTimeOffset Departure = new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryGatePlanStore _store;
    private readonly GateEventHandler _handler;

    public GateEventHandlerTests()
    {
        var plan = GatePlan.Create(
            [Gate("1", AircraftSize.C), Gate("2", AircraftSize.E)],
            TimeSpan.FromMinutes(15)).Match(p => p, e => throw new InvalidOperationException(e.Message));
        _store = new InMemoryGatePlanStore(plan);
        _handler = new GateEventHandler(_store, Options.Create(new FlightEventsOptions()));
    }

    [Fact]
    public void Scheduled_departure_gets_the_smallest_free_gate_before_departure()
    {
        var outcome = _handler.Handle(Event(FlightEventType.Scheduled));

        Assert.Equal(new HandleOutcome.Applied(nameof(AllocateGate)), outcome);
        var allocation = Allocation("BA123");
        Assert.Equal("1", allocation.Gate.Value);
        Assert.Equal(Departure.AddMinutes(-45), allocation.Turn.Window.Start);
        Assert.Equal(Departure, allocation.Turn.Window.End);
    }

    [Fact]
    public void Delay_moves_the_turn_later()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));

        var outcome = _handler.Handle(Event(FlightEventType.Delayed) with { DelayMinutes = 30 });

        Assert.Equal(new HandleOutcome.Applied(nameof(RetimeTurn)), outcome);
        Assert.Equal(Departure.AddMinutes(30), Allocation("BA123").Turn.Window.End);
    }

    [Theory]
    [InlineData(FlightEventType.Departed)]
    [InlineData(FlightEventType.Cancelled)]
    public void Departed_or_cancelled_frees_the_gate(FlightEventType type)
    {
        _handler.Handle(Event(FlightEventType.Scheduled));

        var outcome = _handler.Handle(Event(type));

        Assert.Equal(new HandleOutcome.Applied(nameof(ReleaseGate)), outcome);
        Assert.Empty(_store.Current().Allocations);
    }

    [Fact]
    public void Boarding_leaves_the_plan_alone()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));
        var before = _store.Current();

        Assert.IsType<HandleOutcome.Ignored>(_handler.Handle(Event(FlightEventType.Boarding)));
        Assert.Same(before, _store.Current());
    }

    [Fact]
    public void Arrivals_are_ignored()
    {
        var outcome = _handler.Handle(Event(FlightEventType.Scheduled) with { Origin = "AMS", Destination = "LHR" });

        Assert.IsType<HandleOutcome.Ignored>(outcome);
        Assert.Empty(_store.Current().Allocations);
    }

    [Fact]
    public void Releasing_an_unknown_flight_is_rejected_not_thrown()
    {
        var outcome = Assert.IsType<HandleOutcome.Rejected>(_handler.Handle(Event(FlightEventType.Departed)));

        Assert.IsType<AllocationNotFound>(outcome.Error);
    }

    [Fact]
    public void Duplicate_scheduled_event_is_rejected()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));

        var outcome = Assert.IsType<HandleOutcome.Rejected>(_handler.Handle(Event(FlightEventType.Scheduled)));

        Assert.IsType<AlreadyAllocated>(outcome.Error);
    }

    [Fact]
    public void No_free_gate_is_rejected()
    {
        _handler.Handle(Event(FlightEventType.Scheduled, "BA1"));
        _handler.Handle(Event(FlightEventType.Scheduled, "BA2"));

        var outcome = Assert.IsType<HandleOutcome.Rejected>(_handler.Handle(Event(FlightEventType.Scheduled, "BA3")));

        Assert.IsType<NoGateAvailable>(outcome.Error);
    }

    [Fact]
    public void Invalid_flight_number_is_rejected()
    {
        var outcome = Assert.IsType<HandleOutcome.Rejected>(_handler.Handle(Event(FlightEventType.Scheduled, "EZY1234")));

        Assert.IsType<InvalidFlightNumber>(outcome.Error);
    }

    [Fact]
    public void Simulator_json_deserialises()
    {
        const string json = """
            {"eventId":"8c6f2f1e-6f7e-4a52-9a43-5b9e0a3c2d11","flightId":"SIM-00001","flightNumber":"BA123",
             "eventType":"Delayed","origin":"LHR","destination":"AMS","scheduledDeparture":"2026-10-12T18:00:00+00:00",
             "occurredAt":"2026-10-12T17:00:00+00:00","delayMinutes":20,"reason":"ATC"}
            """;

        var evt = System.Text.Json.JsonSerializer.Deserialize<FlightEventMessage>(json, FlightEventMessage.JsonOptions);

        Assert.NotNull(evt);
        Assert.Equal(FlightEventType.Delayed, evt.EventType);
        Assert.Equal(20, evt.DelayMinutes);
    }

    private Allocation Allocation(string flight) =>
        Assert.IsType<Option<Allocation>.Some>(
            _store.Current().AllocationFor(FlightNumber.Parse(flight).Match(f => f, e => throw new InvalidOperationException()))).Value;

    private static FlightEventMessage Event(FlightEventType type, string flight = "BA123") =>
        new(Guid.NewGuid(), "SIM-00001", flight, type, "LHR", "AMS", Departure, Departure.AddHours(-1));

    private static Gate Gate(string id, AircraftSize size) =>
        new(GateId.Parse(id).Match(g => g, e => throw new InvalidOperationException()), size, new GateStatus.Open());
}
