using AeroFlow.GateAllocation.Domain;
using AeroFlow.GateAllocation.Messaging;
using AeroFlow.GateAllocation.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace AeroFlow.GateAllocation.Tests.Messaging;

public sealed class GateEventPublishingTests
{
    private static readonly DateTimeOffset Departure = new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Departure.AddHours(-2);

    private readonly InMemoryGatePlanStore _store;
    private readonly GateEventHandler _handler;

    public GateEventPublishingTests()
    {
        var plan = GatePlan.Create(
            [Gate("1", AircraftSize.C), Gate("2", AircraftSize.E)],
            TimeSpan.FromMinutes(15)).Match(p => p, e => throw new InvalidOperationException(e.Message));
        _store = new InMemoryGatePlanStore(plan);
        _handler = new GateEventHandler(_store, Options.Create(new FlightEventsOptions()), new FakeTimeProvider(Now));
    }

    [Fact]
    public void Scheduled_publishes_gate_assigned()
    {
        _handler.Handle(Event(FlightEventType.Scheduled), out var events);

        var evt = Assert.Single(events);
        Assert.Equal((GateEventType.GateAssigned, "BA123", "SIM-00001", "1", Now), (evt.EventType, evt.FlightNumber, evt.FlightId, evt.Gate, evt.OccurredAt));
        Assert.Equal((Departure.AddMinutes(-45), Departure), (evt.OnBlock!.Value, evt.OffBlock!.Value));
        Assert.Null(evt.PreviousGate);
    }

    [Fact]
    public void Delay_publishes_gate_changed_with_new_window_and_same_gate()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));

        _handler.Handle(Event(FlightEventType.Delayed) with { DelayMinutes = 30 }, out var events);

        var evt = Assert.Single(events);
        Assert.Equal((GateEventType.GateChanged, "1", Departure.AddMinutes(30)), (evt.EventType, evt.Gate, evt.OffBlock!.Value));
        Assert.Null(evt.PreviousGate);
    }

    [Theory]
    [InlineData(FlightEventType.Departed)]
    [InlineData(FlightEventType.Cancelled)]
    public void Departed_or_cancelled_publishes_gate_released(FlightEventType type)
    {
        _handler.Handle(Event(FlightEventType.Scheduled));

        _handler.Handle(Event(type), out var events);

        var evt = Assert.Single(events);
        Assert.Equal((GateEventType.GateReleased, "1"), (evt.EventType, evt.Gate));
    }

    [Fact]
    public void Rejected_and_ignored_events_publish_nothing()
    {
        _handler.Handle(Event(FlightEventType.Departed), out var rejected);
        _handler.Handle(Event(FlightEventType.Boarding), out var ignored);

        Assert.Empty(rejected);
        Assert.Empty(ignored);
    }

    [Fact]
    public void Reassignment_reports_the_previous_gate()
    {
        var flight = FlightNumber.Parse("BA123").Match(f => f, e => throw new InvalidOperationException());
        _handler.Handle(Event(FlightEventType.Scheduled));
        var before = _store.Current();
        var after = GatePlanner.Apply(before, new ReassignGate(flight, GateId.Parse("2").Match(g => g, e => throw new InvalidOperationException())))
            .Match(p => p, e => throw new InvalidOperationException(e.Message));

        var evt = Assert.Single(GateEvents.Diff(before, after, flight, "SIM-00001", Now, Guid.NewGuid()));

        Assert.Equal((GateEventType.GateChanged, "2", "1"), (evt.EventType, evt.Gate, evt.PreviousGate));
    }

    [Fact]
    public void Service_bus_message_has_subject_and_correlation()
    {
        var evt = new GateEvent(Guid.NewGuid(), GateEventType.GateAssigned, "BA123", "SIM-00001", "12", Now);

        var message = ServiceBusGateEventPublisher.ToMessage(evt);

        Assert.Equal(("GateAssigned", "SIM-00001", evt.EventId.ToString()), (message.Subject, message.CorrelationId, message.MessageId));
        var json = message.Body.ToString();
        Assert.Contains("\"eventType\":\"GateAssigned\"", json, StringComparison.Ordinal);
        Assert.Contains("\"gate\":\"12\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("previousGate", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Event_id_is_derived_from_the_flight_event_and_type()
    {
        var scheduled = Event(FlightEventType.Scheduled);

        _handler.Handle(scheduled, out var events);

        var evt = Assert.Single(events);
        Assert.Equal(EventIds.Derive(scheduled.EventId, nameof(GateEventType.GateAssigned)), evt.EventId);
        Assert.NotEqual(scheduled.EventId, evt.EventId);
    }

    [Fact]
    public void Derived_ids_are_stable_and_distinct_per_name()
    {
        var source = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        var a = EventIds.Derive(source, "GateAssigned");

        Assert.Equal(a, EventIds.Derive(source, "GateAssigned"));
        Assert.NotEqual(a, EventIds.Derive(source, "GateReleased"));
        Assert.NotEqual(a, EventIds.Derive(Guid.NewGuid(), "GateAssigned"));
        Assert.Equal('5', a.ToString()[14]); // RFC 9562 version 5
    }

    [Fact]
    public void Derived_ids_are_pinned_so_renames_cannot_change_them()
    {
        // Ids derive from the type name; renaming a GateEventType member would change ids across a deploy.
        var source = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        Assert.Equal(["GateAssigned", "GateChanged", "GateReleased"], Enum.GetNames<GateEventType>());
        _handler.Handle(Event(FlightEventType.Scheduled) with { EventId = source }, out var events);

        Assert.Equal(Guid.Parse("562baf3b-0b9f-550b-9e3d-71d75580bfa4"), Assert.Single(events).EventId);
    }

    private static FlightEventMessage Event(FlightEventType type) =>
        new(Guid.NewGuid(), "SIM-00001", "BA123", type, "LHR", "AMS", Departure, Departure.AddHours(-1));

    private static Gate Gate(string id, AircraftSize size) =>
        new(GateId.Parse(id).Match(g => g, e => throw new InvalidOperationException()), size, new GateStatus.Open());
}
