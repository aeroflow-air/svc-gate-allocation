namespace AeroFlow.GateAllocation.Domain;

/// <summary>
/// Typed failure with a stable machine-readable <see cref="Code"/> and a human message.
/// Three families, which the HTTP edge maps to 400 / 404 / 409 respectively.
/// </summary>
public abstract record Error(string Code, string Message);

/// <summary>Input was malformed or failed a value rule (maps to 400).</summary>
public abstract record ValidationError(string Code, string Message) : Error(Code, Message);

/// <summary>Something the request refers to does not exist (maps to 404).</summary>
public abstract record NotFoundError(string Code, string Message) : Error(Code, Message);

/// <summary>Input was well formed but breaks a gate allocation rule (maps to 409).</summary>
public abstract record RuleViolation(string Code, string Message) : Error(Code, Message);

// --- Validation -----------------------------------------------------------------

public sealed record InvalidFlightNumber(string? Value)
    : ValidationError("invalid_flight_number", $"'{Value}' is not a valid flight number (e.g. AF204).");

public sealed record InvalidGateId(string? Value)
    : ValidationError("invalid_gate", $"'{Value}' is not a valid gate (expected 1-4 letters or digits).");

public sealed record InvalidAircraftSize(string? Value)
    : ValidationError("invalid_aircraft_size", $"'{Value}' is not a valid aircraft size (expected ICAO code C, D, E or F).");

public sealed record EmptyTimeWindow(DateTimeOffset Start, DateTimeOffset End)
    : ValidationError("empty_time_window", $"Off-block {End:u} must be after on-block {Start:u}.");

public sealed record NegativeTurnaround(TimeSpan Value)
    : ValidationError("negative_turnaround", $"Minimum turnaround {Value} cannot be negative.");

public sealed record DuplicateGate(GateId Gate)
    : ValidationError("duplicate_gate", $"Gate {Gate} is listed more than once.");

// --- Not found ------------------------------------------------------------------

public sealed record GateNotFound(GateId Gate)
    : NotFoundError("gate_not_found", $"No gate exists with id '{Gate}'.");

public sealed record AllocationNotFound(FlightNumber Flight)
    : NotFoundError("allocation_not_found", $"Flight {Flight} has no gate allocation.");

// --- Allocation rules -----------------------------------------------------------

public sealed record AlreadyAllocated(FlightNumber Flight, GateId Gate)
    : RuleViolation("already_allocated", $"Flight {Flight} is already on gate {Gate}; reassign it instead.");

public sealed record AlreadyAtGate(FlightNumber Flight, GateId Gate)
    : RuleViolation("already_at_gate", $"Flight {Flight} is already on gate {Gate}.");

public sealed record GateClosed(GateId Gate, string Reason)
    : RuleViolation("gate_closed", $"Gate {Gate} is closed: {Reason}.");

public sealed record GateNotClosed(GateId Gate)
    : RuleViolation("gate_not_closed", $"Gate {Gate} is already open.");

public sealed record GateHasAllocations(GateId Gate, int Count)
    : RuleViolation("gate_has_allocations", $"Gate {Gate} still has {Count} allocation(s); move them before closing it.");

public sealed record AircraftTooLarge(GateId Gate, AircraftSize GateMax, AircraftSize Aircraft)
    : RuleViolation("aircraft_too_large", $"Gate {Gate} takes up to code {GateMax}; the aircraft is code {Aircraft}.");

public sealed record GateConflict(GateId Gate, FlightNumber Occupant, TimeWindow OccupantWindow, TimeSpan Turnaround)
    : RuleViolation("gate_conflict", $"Gate {Gate} is taken by {Occupant} from {OccupantWindow} (plus {Turnaround.TotalMinutes:0} min turnaround).");

public sealed record NoGateAvailable(FlightNumber Flight)
    : RuleViolation("no_gate_available", $"No open gate fits flight {Flight} for that window.");
