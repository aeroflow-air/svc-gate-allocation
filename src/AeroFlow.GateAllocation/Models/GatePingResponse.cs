namespace AeroFlow.GateAllocation.Models;

public sealed record GatePingResponse(
    string Service,
    string Message,
    DateTimeOffset UtcNow);
