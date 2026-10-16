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
    public void Mark_published_removes_only_the_exact_head_entry()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));
        var head = _store.Pending()[0];

        _store.MarkPublished(head with { }); // same id and values, but not the stored entry

        Assert.Single(_store.Pending());
        _store.MarkPublished(head);
        Assert.Empty(_store.Pending());
    }

    [Fact]
    public async Task Old_delay_redelivered_after_a_newer_one_is_ignored_and_nothing_is_lost()
    {
        _handler.Handle(Event(FlightEventType.Scheduled));
        var first = Event(FlightEventType.Delayed) with { DelayMinutes = 30 };
        var second = Event(FlightEventType.Delayed) with { DelayMinutes = 60 };
        _handler.Handle(first);
        _handler.Handle(second);

        // A-B-A: the first Delayed is redelivered (lock lost before Complete) while the bus is down.
        Assert.IsType<HandleOutcome.Ignored>(_handler.Handle(first));

        await _dispatcher.DrainAsync(CancellationToken.None);
        Assert.Equal(
            [GateEventType.GateAssigned, GateEventType.GateChanged, GateEventType.GateChanged],
            _publisher.Sent.Select(e => e.EventType));
        Assert.Equal(3, _publisher.Sent.Select(e => e.EventId).Distinct().Count());
        Assert.Equal(Departure.AddMinutes(60), _publisher.Sent[^1].OffBlock);
        Assert.Equal(Departure.AddMinutes(60), _store.Current().Allocations.Values.Single().Turn.Window.End);
    }

    [Fact]
    public void Derive_matches_the_rfc_uuid_v5_test_vector()
    {
        // uuid5(NAMESPACE_DNS, "www.example.com") from RFC 9562 / Python's uuid module: pins ids across versions.
        var dns = Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        Assert.Equal(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"), EventIds.Derive(dns, "www.example.com"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Flight_event_without_identity_is_rejected_and_queues_nothing(bool emptyEventId, bool emptyFlightId)
    {
        var evt = Event(FlightEventType.Scheduled);
        evt = evt with { EventId = emptyEventId ? Guid.Empty : evt.EventId, FlightId = emptyFlightId ? "" : evt.FlightId };

        var outcome = Assert.IsType<HandleOutcome.Rejected>(_handler.Handle(evt));

        Assert.Equal("missing_event_identity", outcome.Error.Code);
        Assert.Empty(_store.Pending());
    }

    [Fact]
    public async Task Running_dispatcher_backs_off_after_a_failure_then_sends()
    {
        var clock = new FakeTimeProvider(Now);
        var dispatcher = new OutboxDispatcher(_store, _publisher, Options.Create(new OutboxOptions { RetrySeconds = 5 }), clock, NullLogger<OutboxDispatcher>.Instance);
        _handler.Handle(Event(FlightEventType.Scheduled));
        _publisher.FailNext = true;

        await dispatcher.StartAsync(CancellationToken.None);
        await WaitUntil(() => _publisher.Attempts == 1);

        dispatcher.Notify(); // a wake-up during the back-off doesn't retry early
        await Task.Delay(50);
        Assert.Equal(1, _publisher.Attempts);

        await AdvanceUntil(clock, () => _store.Pending().Count == 0);
        Assert.Single(_publisher.Sent);

        // A publisher timeout that isn't a shutdown must not stop the dispatcher.
        _publisher.ThrowCancelled = true;
        _handler.Handle(Event(FlightEventType.Departed));
        dispatcher.Notify();
        await WaitUntil(() => _publisher.Attempts == 3);
        await AdvanceUntil(clock, () => _store.Pending().Count == 0);
        Assert.Equal(2, _publisher.Sent.Count);

        await dispatcher.StopAsync(CancellationToken.None);
    }

    /// <summary>Moves the fake clock in retry-sized steps until the condition holds, so no timer registration race.</summary>
    private static async Task AdvanceUntil(FakeTimeProvider clock, Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static FlightEventMessage Event(FlightEventType type) =>
        new(Guid.NewGuid(), "SIM-00001", "BA123", type, "LHR", "AMS", Departure, Departure.AddHours(-1));

    private sealed class FlakyPublisher : IGateEventPublisher
    {
        public bool FailNext { get; set; }

        public bool ThrowCancelled { get; set; }

        public int Attempts { get; private set; }

        public List<GateEvent> Sent { get; } = [];

        public Task PublishAsync(IReadOnlyCollection<GateEvent> events, CancellationToken cancellationToken)
        {
            Attempts++;
            if (ThrowCancelled)
            {
                ThrowCancelled = false;
                throw new TaskCanceledException("send timed out");
            }

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
