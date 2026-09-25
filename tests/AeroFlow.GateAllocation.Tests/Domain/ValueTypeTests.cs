using AeroFlow.GateAllocation.Domain;
using Xunit;
using static AeroFlow.GateAllocation.Tests.Domain.TestPlans;

namespace AeroFlow.GateAllocation.Tests.Domain;

public sealed class ValueTypeTests
{
    [Theory]
    [InlineData("AF204", "AF204")]
    [InlineData(" af9 ", "AF9")]
    [InlineData("U21234", "U21234")]
    public void Flight_number_parses_and_normalises(string input, string expected)
    {
        Assert.Equal(expected, AssertResult.Ok(FlightNumber.Parse(input)).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AF")]
    [InlineData("AF12345")]
    [InlineData("AFX12")]
    public void Flight_number_rejects_bad_input(string? input)
    {
        Assert.Equal("invalid_flight_number", AssertResult.Failure<InvalidFlightNumber, FlightNumber>(FlightNumber.Parse(input)).Code);
    }

    [Theory]
    [InlineData("12", "12")]
    [InlineData(" 55a ", "55A")]
    [InlineData("R1", "R1")]
    public void Gate_id_parses_and_normalises(string input, string expected)
    {
        Assert.Equal(expected, AssertResult.Ok(GateId.Parse(input)).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1-2")]
    public void Gate_id_rejects_bad_input(string? input)
    {
        AssertResult.Failure<InvalidGateId, GateId>(GateId.Parse(input));
    }

    [Fact]
    public void Value_types_compare_by_value()
    {
        Assert.Equal(Id("55a"), Id("55A"));
        Assert.Equal(Flight("af204"), Flight("AF204"));
    }

    [Theory]
    [InlineData("c", AircraftSize.C)]
    [InlineData("F", AircraftSize.F)]
    public void Aircraft_size_parses_icao_letters(string input, AircraftSize expected)
    {
        Assert.Equal(expected, AssertResult.Ok(AircraftSizes.Parse(input)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("B")]
    [InlineData("0")]
    [InlineData("CodeC")]
    public void Aircraft_size_rejects_anything_else(string? input)
    {
        AssertResult.Failure<InvalidAircraftSize, AircraftSize>(AircraftSizes.Parse(input));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Time_window_must_end_after_it_starts(int lengthMinutes)
    {
        AssertResult.Failure<EmptyTimeWindow, TimeWindow>(TimeWindow.Create(T0, T0.AddMinutes(lengthMinutes)));
    }

    [Theory]
    [InlineData(0, 60, 30, 90, true)] // plain overlap
    [InlineData(0, 60, 70, 90, true)] // inside the 15 min buffer after
    [InlineData(70, 90, 0, 60, true)] // same, other way round
    [InlineData(0, 60, 75, 90, false)] // exactly the buffer apart
    [InlineData(0, 60, 100, 120, false)]
    public void Time_windows_overlap_within_the_buffer(int s1, int e1, int s2, int e2, bool expected)
    {
        Assert.Equal(expected, Window(s1, e1).Overlaps(Window(s2, e2), Turnaround));
        Assert.Equal(expected, Window(s2, e2).Overlaps(Window(s1, e1), Turnaround));
    }

    [Fact]
    public void Plan_rejects_duplicate_gates_and_negative_turnaround()
    {
        AssertResult.Failure<DuplicateGate, GatePlan>(GatePlan.Create([Gate("1", AircraftSize.C), Gate("1", AircraftSize.E)], Turnaround));
        AssertResult.Failure<NegativeTurnaround, GatePlan>(GatePlan.Create([Gate("1", AircraftSize.C)], TimeSpan.FromMinutes(-1)));
    }
}
