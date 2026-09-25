namespace AeroFlow.GateAllocation.Domain;

/// <summary>Flight number such as <c>AF204</c>. Only constructible via <see cref="Parse"/>.</summary>
public sealed record FlightNumber
{
    private FlightNumber(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Two-character airline designator followed by 1-4 digits (surrounding whitespace ignored),
    /// normalised to upper case. Same shape as flight-status uses.
    /// </summary>
    public static Result<FlightNumber> Parse(string? value) =>
        value?.Trim().ToUpperInvariant() is { Length: >= 3 and <= 6 } v
        && v[..2].All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c))
        && v[2..].All(char.IsAsciiDigit)
            ? new FlightNumber(v)
            : new InvalidFlightNumber(value);

    public override string ToString() => Value;
}
