namespace AeroFlow.GateAllocation.Domain;

/// <summary>Gate identifier such as <c>12</c> or <c>55A</c>. Only constructible via <see cref="Parse"/>.</summary>
public sealed record GateId
{
    private GateId(string value) => Value = value;

    public string Value { get; }

    /// <summary>Accepts 1-4 ASCII letters or digits (surrounding whitespace ignored), normalised to upper case.</summary>
    public static Result<GateId> Parse(string? value) =>
        value?.Trim() is { Length: >= 1 and <= 4 } trimmed && trimmed.All(char.IsAsciiLetterOrDigit)
            ? new GateId(trimmed.ToUpperInvariant())
            : new InvalidGateId(value);

    public override string ToString() => Value;
}
