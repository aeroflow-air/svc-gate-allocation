using System.Text.Json;
using AeroFlow.GateAllocation.Domain;
using AeroFlow.GateAllocation.Messaging;
using AeroFlow.GateAllocation.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace AeroFlow.GateAllocation.Tests.Messaging;

/// <summary>
/// Consumer contract tests for flight-events (ADR-0019, platform-handbook#111). The JSON files under
/// Contracts/flight-events are pinned copies of the producer's canonical examples in aeroflow-local
/// (contracts/flight-events). Do not edit them here: change the producer first, then copy the new
/// examples over these pins (see aeroflow-local/contracts/README.md).
/// </summary>
public sealed class FlightEventContractTests
{
    public static TheoryData<string, FlightEventType> Examples => new()
    {
        { "scheduled", FlightEventType.Scheduled },
        { "boarding", FlightEventType.Boarding },
        { "departed", FlightEventType.Departed },
        { "landed", FlightEventType.Landed },
        { "delayed", FlightEventType.Delayed },
        { "cancelled", FlightEventType.Cancelled },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void Canonical_example_deserialises_into_the_local_copy(string name, FlightEventType type)
    {
        var evt = Read(name);

        Assert.Equal(type, evt.EventType);
        Assert.False(string.IsNullOrWhiteSpace(evt.FlightId));
        Assert.False(string.IsNullOrWhiteSpace(evt.FlightNumber));
        Assert.False(string.IsNullOrWhiteSpace(evt.Origin));
        Assert.False(string.IsNullOrWhiteSpace(evt.Destination));
        Assert.NotEqual(default, evt.ScheduledDeparture);
        Assert.NotEqual(default, evt.OccurredAt);
    }

    [Fact]
    public void Every_flight_event_type_has_a_pinned_example()
    {
        var covered = Examples.Select(row => (FlightEventType)row[1]!).ToHashSet();
        Assert.Equal(Enum.GetValues<FlightEventType>().ToHashSet(), covered);
    }

    [Fact]
    public void Optional_fields_match_the_contract()
    {
        Assert.Equal((35, "Weather"), (Read("delayed").DelayMinutes, Read("delayed").Reason));
        Assert.Equal(((int?)null, (string?)null), (Read("scheduled").DelayMinutes, Read("scheduled").Reason));
    }

    [Fact]
    public void Unknown_extra_field_is_tolerated()
    {
        var json = ReadText("scheduled");
        var withExtra = json.Insert(json.TrimEnd().Length - 1, ", \"futureField\": {\"anything\": 1}");

        Assert.Equal(FlightEventType.Scheduled, JsonSerializer.Deserialize<FlightEventMessage>(withExtra, FlightEventMessage.JsonOptions)!.EventType);
    }

    [Fact]
    public void Unknown_event_type_fails_deserialisation_and_would_dead_letter()
    {
        // ADR-0019: a new event type is breaking and consumer-first. The processors in this repo
        // dead-letter bodies that fail deserialisation as "unreadable"; this copy must keep failing
        // loudly on a type it does not know rather than silently accepting it.
        var json = ReadText("scheduled").Replace("\"Scheduled\"", "\"Refuelled\"");

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FlightEventMessage>(json, FlightEventMessage.JsonOptions));
    }

    [Fact]
    public void Canonical_scheduled_example_is_handled()
    {
        var plan = GatePlan.Create(
            [Gate("1", AircraftSize.C), Gate("2", AircraftSize.E)],
            TimeSpan.FromMinutes(15)).Match(p => p, e => throw new InvalidOperationException(e.Message));
        var handler = new GateEventHandler(new InMemoryGatePlanStore(plan), Options.Create(new FlightEventsOptions()));

        Assert.IsType<HandleOutcome.Applied>(handler.Handle(Read("scheduled")));
    }

    private static Gate Gate(string id, AircraftSize size) =>
        new(GateId.Parse(id).Match(g => g, e => throw new InvalidOperationException()), size, new GateStatus.Open());

    private static FlightEventMessage Read(string name) =>
        JsonSerializer.Deserialize<FlightEventMessage>(ReadText(name), FlightEventMessage.JsonOptions)!;

    private static string ReadText(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "flight-events", $"{name}.json"));
}
