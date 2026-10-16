using AeroFlow.GateAllocation.Domain;
using AeroFlow.GateAllocation.Messaging;
using AeroFlow.GateAllocation.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace AeroFlow.GateAllocation.Tests.Messaging;

/// <summary>ADR-0017: gate events survive a failed publish and are sent once the bus is back.</summary>
public sealed class OutboxTests
{
    private static readonly DateTimeOffset Departure = new(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Departure.AddHours(-2);

    private readonly InMemoryGatePlanStore _store;
    private readonly GateEventHandler _handler;
    private readonly FlakyPublisher _publisher = new();
    private readonly OutboxDispatcher _dispatcher;

    public OutboxTests()
    {
        var plan = GatePlan.Create(
            [new Gate(GateId.Parse("1").Match(g => g, e => throw new InvalidOperationException()), AircraftSize.C, new GateStatus.Open())],
            TimeSpan.FromMinutes(15)).Match(p => p, e => throw new InvalidOperationException(e.Message));
        _store = new InMemoryGatePlanStore(plan);
        var clock = new FakeTimeProvider(Now);
        _handler = new GateEventHandler(_store, Options.Create(new FlightEventsOptions()), clock);
        _dispatcher = new OutboxDispatcher(_store, _publisher, Options.Create(new OutboxOptions()), clock, NullLogger<OutboxDispatcher>.Instance);
    }

    [Fact]
    public void Applied_change_queues_its_events_in_the_outbox()
    {
        _handler.Handle(Event(FlightEventType.Scheduled), out var events);

        Assert.Equal(events, _store.Pending());
    }

    [Fact]
    public async Task Drain_sends_and_clears_the_outbox()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));

        Assert.Equal(1, await _dispatcher.DrainAsync(CancellationToken.None));

        Assert.Empty(_store.Pending());
        Assert.Equal(GateEventType.GateAssigned, Assert.Single(_publisher.Sent).EventType);
    }

    [Fact]
    public async Task Failed_publish_keeps_the_event_and_a_retry_sends_it()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));
        _publisher.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _dispatcher.DrainAsync(CancellationToken.None));
        var pending = Assert.Single(_store.Pending());

        // The redelivered flight event is a repeat (AlreadyAllocated) and queues nothing new...
        Assert.IsType<HandleOutcome.Rejected>(_handler.Handle(Event(FlightEventType.Scheduled)));
        Assert.Single(_store.Pending());

        // ...but the retry still sends the original event, with its original id.
        await _dispatcher.DrainAsync(CancellationToken.None);
        Assert.Equal(pending.EventId, Assert.Single(_publisher.Sent).EventId);
        Assert.Empty(_store.Pending());
    }

    [Fact]
    public async Task Events_are_sent_in_order_and_a_failure_holds_back_later_ones()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));
        _handler.Handle(Event(FlightEventType.Departed));
        _publisher.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _dispatcher.DrainAsync(CancellationToken.None));
        Assert.Equal(2, _store.Pending().Count);

        await _dispatcher.DrainAsync(CancellationToken.None);
        Assert.Equal([GateEventType.GateAssigned, GateEventType.GateReleased], _publisher.Sent.Select(e => e.EventType));
    }

    [Fact]
    public void Rejected_change_queues_nothing()
    {
        _handler.Handle(Event(FlightEventType.Departed));

        Assert.Empty(_store.Pending());
    }

    [Fact]
    public void Mark_published_ignores_unknown_ids()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));

        _store.MarkPublished(Guid.NewGuid());

        Assert.Single(_store.Pending());
    }

    private static FlightEventMessage Event(FlightEventType type) =>
        new(Guid.NewGuid(), "SIM-00001", "BA123", type, "LHR", "AMS", Departure, Departure.AddHours(-1));

    private sealed class FlakyPublisher : IGateEventPublisher
    {
        public bool FailNext { get; set; }

        public List<GateEvent> Sent { get; } = [];

        public Task PublishAsync(IReadOnlyCollection<GateEvent> events, CancellationToken cancellationToken)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("bus down");
            }

            Sent.AddRange(events);
            return Task.CompletedTask;
        }
    }
}
