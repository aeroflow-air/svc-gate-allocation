using System.Text.Json;
using AeroFlow.GateAllocation.Domain;
using Xunit;

namespace AeroFlow.GateAllocation.Tests.Domain;

/// <summary>
/// Producer-side contract tests for gate-events (ADR-0019, platform-handbook#111): the canonical examples
/// in contracts/gate-events must round-trip through GateEvent with no structural drift.
/// </summary>
public sealed class GateEventContractTests
{
    public static TheoryData<string, GateEventType> Examples => new()
    {
        { "gate-assigned", GateEventType.GateAssigned },
        { "gate-changed", GateEventType.GateChanged },
        { "gate-released", GateEventType.GateReleased },
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void Canonical_example_round_trips_with_the_same_structure(string name, GateEventType type)
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "contracts", "gate-events", $"{name}.json"));
        var evt = JsonSerializer.Deserialize<GateEvent>(json, GateEvent.JsonOptions);

        Assert.NotNull(evt);
        Assert.Equal((type, "SIM-00001", "BA123"), (evt!.EventType, evt.FlightId, evt.FlightNumber));

        using var canonical = JsonDocument.Parse(json);
        using var roundTripped = JsonDocument.Parse(JsonSerializer.Serialize(evt, GateEvent.JsonOptions));
        Assert.Equal(
            canonical.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind),
            roundTripped.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind));
    }

    [Fact]
    public void Every_gate_event_type_has_a_canonical_example()
    {
        var covered = Examples.Select(row => (GateEventType)row[1]!).ToHashSet();
        Assert.Equal(Enum.GetValues<GateEventType>().ToHashSet(), covered);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AeroFlow.GateAllocation.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
