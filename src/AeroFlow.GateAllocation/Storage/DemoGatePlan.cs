using AeroFlow.GateAllocation.Domain;

namespace AeroFlow.GateAllocation.Storage;

/// <summary>
/// Demo seed data for a small pier, built relative to "now" so the plan looks current. Gates are created with
/// <see cref="GatePlan.Create"/> and allocations are made by applying real <see cref="GateCommand"/>s.
/// Flight numbers match the flight-status demo where they depart from LGW.
/// </summary>
public static class DemoGatePlan
{
    public static readonly TimeSpan MinimumTurnaround = TimeSpan.FromMinutes(15);

    public static GatePlan Create(DateTimeOffset now)
    {
        // Round down to five minutes, in UTC, so times read like a real timetable.
        var utc = now.ToUniversalTime();
        var t0 = new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.FromMinutes(5).Ticks), TimeSpan.Zero);
        TimeSpan Min(int minutes) => TimeSpan.FromMinutes(minutes);

        Gate[] gates =
        [
            Gate("7", AircraftSize.C),
            Gate("9", AircraftSize.C),
            Gate("12", AircraftSize.D),
            Gate("14", AircraftSize.E),
            Gate("21", AircraftSize.C),
            Gate("55A", AircraftSize.F),
            Gate("R1", AircraftSize.C),
        ];

        GateCommand[] commands =
        [
            Allocate("AF204", AircraftSize.C, t0 - Min(5), t0 + Min(45), "12"),
            Allocate("AF310", AircraftSize.C, t0 - Min(25), t0 + Min(20), "21"),
            Allocate("AF455", AircraftSize.D, t0 + Min(75), t0 + Min(120), "12"),
            Allocate("AF901", AircraftSize.E, t0 + Min(30), t0 + Min(130), "14"),
            Allocate("AF733", AircraftSize.C, t0 + Min(40), t0 + Min(90), gate: null),
            new CloseGate(Id("R1"), "Resurfacing"),
        ];

        return GatePlan.Create(gates, MinimumTurnaround)
            .Bind(plan => commands.Aggregate((Result<GatePlan>)plan, (acc, cmd) => acc.Bind(p => GatePlanner.Apply(p, cmd))))
            .Match(plan => plan, error => throw new InvalidOperationException($"Invalid demo gate plan: {error.Message}"));
    }

    private static AllocateGate Allocate(string flight, AircraftSize size, DateTimeOffset onBlock, DateTimeOffset offBlock, string? gate) =>
        new(
            new Turn(Ok(FlightNumber.Parse(flight)), size, Ok(TimeWindow.Create(onBlock, offBlock))),
            gate is null ? Option.None<GateId>() : Option.Some(Id(gate)));

    private static Gate Gate(string id, AircraftSize maxSize) => new(Id(id), maxSize, new GateStatus.Open());

    private static GateId Id(string id) => Ok(GateId.Parse(id));

    // Seed data is hard-coded, so an invalid value is a programming error rather than a business outcome.
    private static T Ok<T>(Result<T> result) =>
        result.Match(value => value, error => throw new InvalidOperationException(error.Message));
}
