using AeroFlow.GateAllocation.Domain;
using Xunit;

namespace AeroFlow.GateAllocation.Tests.Domain;

internal static class TestPlans
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
    public static readonly TimeSpan Turnaround = TimeSpan.FromMinutes(15);

    public static FlightNumber Flight(string number) => AssertResult.Ok(FlightNumber.Parse(number));

    public static GateId Id(string gate) => AssertResult.Ok(GateId.Parse(gate));

    /// <summary>Window from <paramref name="startMinutes"/> to <paramref name="endMinutes"/> after <see cref="T0"/>.</summary>
    public static TimeWindow Window(int startMinutes, int endMinutes) =>
        AssertResult.Ok(TimeWindow.Create(T0.AddMinutes(startMinutes), T0.AddMinutes(endMinutes)));

    public static Turn Turn(string flight, int startMinutes, int endMinutes, AircraftSize size = AircraftSize.C) =>
        new(Flight(flight), size, Window(startMinutes, endMinutes));

    public static Gate Gate(string id, AircraftSize maxSize) => new(Id(id), maxSize, new GateStatus.Open());

    /// <summary>Gates 1 (C), 2 (C), 3 (E) and nothing allocated.</summary>
    public static GatePlan Empty() =>
        AssertResult.Ok(GatePlan.Create([Gate("1", AircraftSize.C), Gate("2", AircraftSize.C), Gate("3", AircraftSize.E)], Turnaround));

    /// <summary>Applies <paramref name="commands"/> in order through the real planner, so fixtures cannot drift from the rules.</summary>
    public static GatePlan With(this GatePlan plan, params GateCommand[] commands) =>
        commands.Aggregate(plan, (p, c) => AssertResult.Ok(GatePlanner.Apply(p, c)));

    public static AllocateGate AllocateTo(string gate, Turn turn) => new(turn, Option.Some(Id(gate)));

    public static AllocateGate AllocateAnywhere(Turn turn) => new(turn, Option.None<GateId>());

    public static GateId GateOf(GatePlan plan, string flight) =>
        plan.AllocationFor(Flight(flight)).Match(a => a.Gate, () => throw new Xunit.Sdk.XunitException($"{flight} has no allocation"));
}

internal static class AssertResult
{
    public static T Ok<T>(Result<T> result) =>
        result.Match(value => value, error => throw new Xunit.Sdk.XunitException($"Expected Ok but got {error}"));

    public static TError Failure<TError, T>(Result<T> result)
        where TError : Error =>
        result.Match(
            value => throw new Xunit.Sdk.XunitException($"Expected {typeof(TError).Name} but got Ok: {value}"),
            error => Assert.IsType<TError>(error));

    public static TError Failure<TError>(Result<GatePlan> result)
        where TError : Error => Failure<TError, GatePlan>(result);
}
