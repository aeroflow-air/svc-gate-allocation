using AeroFlow.GateAllocation.Domain;
using Xunit;
using static AeroFlow.GateAllocation.Tests.Domain.TestPlans;

namespace AeroFlow.GateAllocation.Tests.Domain;

public sealed class GatePlannerTests
{
    // --- Allocate to a named gate ---------------------------------------------------

    [Fact]
    public void Allocating_to_a_free_gate_records_the_allocation()
    {
        var plan = AssertResult.Ok(GatePlanner.Apply(Empty(), AllocateTo("1", Turn("AF204", 0, 60))));

        var allocation = Assert.IsType<Option<Allocation>.Some>(plan.AllocationFor(Flight("AF204"))).Value;
        Assert.Equal(Id("1"), allocation.Gate);
        Assert.Equal(Window(0, 60), allocation.Turn.Window);
        Assert.Equal([allocation], plan.AllocationsAt(Id("1")));
    }

    [Fact]
    public void The_original_plan_is_unchanged()
    {
        var before = Empty();

        AssertResult.Ok(GatePlanner.Apply(before, AllocateTo("1", Turn("AF204", 0, 60))));

        Assert.Empty(before.Allocations);
    }

    [Fact]
    public void Unknown_gate_is_not_found()
    {
        var error = AssertResult.Failure<GateNotFound>(GatePlanner.Apply(Empty(), AllocateTo("99", Turn("AF204", 0, 60))));

        Assert.Equal("gate_not_found", error.Code);
    }

    [Fact]
    public void A_flight_can_hold_only_one_allocation()
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)));

        var error = AssertResult.Failure<AlreadyAllocated>(GatePlanner.Apply(plan, AllocateTo("2", Turn("AF204", 0, 60))));

        Assert.Equal(Id("1"), error.Gate);
    }

    [Fact]
    public void Aircraft_must_fit_the_gate()
    {
        var error = AssertResult.Failure<AircraftTooLarge>(
            GatePlanner.Apply(Empty(), AllocateTo("1", Turn("AF901", 0, 60, AircraftSize.E))));

        Assert.Equal((AircraftSize.C, AircraftSize.E), (error.GateMax, error.Aircraft));
    }

    [Fact]
    public void Smaller_aircraft_fit_bigger_gates()
    {
        AssertResult.Ok(GatePlanner.Apply(Empty(), AllocateTo("3", Turn("AF204", 0, 60, AircraftSize.C))));
    }

    [Theory]
    [InlineData(30, 90)] // overlaps
    [InlineData(70, 120)] // inside turnaround after
    [InlineData(-60, -10)] // inside turnaround before
    public void Two_flights_cannot_share_a_gate_within_the_turnaround(int start, int end)
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)));

        var error = AssertResult.Failure<GateConflict>(GatePlanner.Apply(plan, AllocateTo("1", Turn("AF310", start, end))));

        Assert.Equal(Flight("AF204"), error.Occupant);
        Assert.Equal("gate_conflict", error.Code);
    }

    [Fact]
    public void Back_to_back_flights_exactly_one_turnaround_apart_are_fine()
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)), AllocateTo("1", Turn("AF310", 75, 120)));

        Assert.Equal([Flight("AF204"), Flight("AF310")], plan.AllocationsAt(Id("1")).Select(a => a.Flight));
    }

    [Fact]
    public void Closed_gate_takes_no_allocations()
    {
        var plan = Empty().With(new CloseGate(Id("1"), "Jet bridge fault"));

        var error = AssertResult.Failure<GateClosed>(GatePlanner.Apply(plan, AllocateTo("1", Turn("AF204", 0, 60))));

        Assert.Equal("Jet bridge fault", error.Reason);
    }

    // --- Allocate anywhere (Suggest) ------------------------------------------------

    [Fact]
    public void Without_a_gate_the_planner_picks_the_smallest_gate_that_fits()
    {
        var plan = AssertResult.Ok(GatePlanner.Apply(Empty(), AllocateAnywhere(Turn("AF204", 0, 60))));

        Assert.Equal(Id("1"), GateOf(plan, "AF204"));
    }

    [Fact]
    public void Suggest_skips_busy_closed_and_too_small_gates()
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)), new CloseGate(Id("2"), "Cleaning"));

        Assert.Equal(Option.Some(Id("3")), GatePlanner.Suggest(plan, Turn("AF310", 30, 90)));
        Assert.Equal(Option.Some(Id("3")), GatePlanner.Suggest(Empty(), Turn("AF901", 0, 60, AircraftSize.E)));
    }

    [Fact]
    public void Suggest_is_none_when_nothing_fits()
    {
        Assert.Equal(Option.None<GateId>(), GatePlanner.Suggest(Empty(), Turn("AF380", 0, 60, AircraftSize.F)));
    }

    [Fact]
    public void Allocating_anywhere_with_nothing_free_is_rejected()
    {
        var error = AssertResult.Failure<NoGateAvailable>(
            GatePlanner.Apply(Empty(), AllocateAnywhere(Turn("AF380", 0, 60, AircraftSize.F))));

        Assert.Equal("no_gate_available", error.Code);
    }

    // --- Reassign / retime / release ------------------------------------------------

    [Fact]
    public void Reassign_moves_the_flight_and_frees_the_old_gate()
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)));

        var moved = AssertResult.Ok(GatePlanner.Apply(plan, new ReassignGate(Flight("AF204"), Id("2"))));

        Assert.Equal(Id("2"), GateOf(moved, "AF204"));
        Assert.Empty(moved.AllocationsAt(Id("1")));
    }

    [Fact]
    public void Reassign_obeys_the_same_rules_as_allocation()
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)), AllocateTo("2", Turn("AF310", 30, 90)));

        AssertResult.Failure<GateConflict>(GatePlanner.Apply(plan, new ReassignGate(Flight("AF204"), Id("2"))));
        AssertResult.Failure<AlreadyAtGate>(GatePlanner.Apply(plan, new ReassignGate(Flight("AF204"), Id("1"))));
        AssertResult.Failure<AllocationNotFound>(GatePlanner.Apply(plan, new ReassignGate(Flight("AF999"), Id("3"))));
    }

    [Fact]
    public void Retime_keeps_the_gate_and_ignores_the_flights_own_old_window()
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)));

        var retimed = AssertResult.Ok(GatePlanner.Apply(plan, new RetimeTurn(Flight("AF204"), Window(20, 80))));

        var allocation = Assert.IsType<Option<Allocation>.Some>(retimed.AllocationFor(Flight("AF204"))).Value;
        Assert.Equal((Id("1"), Window(20, 80)), (allocation.Gate, allocation.Turn.Window));
    }

    [Fact]
    public void Retime_into_the_next_flights_turn_is_a_conflict()
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)), AllocateTo("1", Turn("AF310", 90, 150)));

        var error = AssertResult.Failure<GateConflict>(GatePlanner.Apply(plan, new RetimeTurn(Flight("AF204"), Window(40, 100))));

        Assert.Equal(Flight("AF310"), error.Occupant);
    }

    [Fact]
    public void Release_removes_the_allocation()
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)));

        var released = AssertResult.Ok(GatePlanner.Apply(plan, new ReleaseGate(Flight("AF204"))));

        Assert.Equal(Option.None<Allocation>(), released.AllocationFor(Flight("AF204")));
        AssertResult.Failure<AllocationNotFound>(GatePlanner.Apply(released, new ReleaseGate(Flight("AF204"))));
    }

    // --- Close / reopen -------------------------------------------------------------

    [Fact]
    public void Cannot_close_a_gate_that_still_has_allocations()
    {
        var plan = Empty().With(AllocateTo("1", Turn("AF204", 0, 60)));

        var error = AssertResult.Failure<GateHasAllocations>(GatePlanner.Apply(plan, new CloseGate(Id("1"), "Maintenance")));

        Assert.Equal(1, error.Count);
    }

    [Fact]
    public void Close_then_reopen_round_trips_the_status()
    {
        var closed = Empty().With(new CloseGate(Id("2"), "Maintenance"));
        Assert.Equal(new GateStatus.Closed("Maintenance"), Gate(closed, "2").Status);

        var reopened = closed.With(new ReopenGate(Id("2")));
        Assert.Equal(new GateStatus.Open(), Gate(reopened, "2").Status);

        AssertResult.Failure<GateNotClosed>(GatePlanner.Apply(reopened, new ReopenGate(Id("2"))));
    }

    private static Gate Gate(GatePlan plan, string id) =>
        Assert.IsType<Option<Gate>.Some>(plan.FindGate(Id(id))).Value;
}
