namespace AeroFlow.GateAllocation.Domain;

/// <summary>
/// Half-open interval [<see cref="Start"/>, <see cref="End"/>) that a flight occupies a gate:
/// on-block to off-block. Only constructible via <see cref="Create"/>, so <c>Start &lt; End</c> always holds.
/// </summary>
public sealed record TimeWindow
{
    private TimeWindow(DateTimeOffset start, DateTimeOffset end) => (Start, End) = (start, end);

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public TimeSpan Duration => End - Start;

    public static Result<TimeWindow> Create(DateTimeOffset start, DateTimeOffset end) =>
        start < end ? new TimeWindow(start, end) : new EmptyTimeWindow(start, end);

    /// <summary>
    /// True when the two windows are closer than <paramref name="buffer"/>, i.e. when one would still be on
    /// the gate (plus turnaround) as the other arrives. Windows that exactly touch the buffer do not overlap.
    /// </summary>
    public bool Overlaps(TimeWindow other, TimeSpan buffer) =>
        Start < other.End + buffer && other.Start < End + buffer;

    public override string ToString() => $"{Start:u} to {End:u}";
}
