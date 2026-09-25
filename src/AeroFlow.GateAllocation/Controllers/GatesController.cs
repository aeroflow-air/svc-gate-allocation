using AeroFlow.GateAllocation.Domain;
using AeroFlow.GateAllocation.Models;
using AeroFlow.GateAllocation.Storage;
using Microsoft.AspNetCore.Mvc;

namespace AeroFlow.GateAllocation.Controllers;

/// <summary>
/// HTTP edge for gates. Side effects (clock, store) live here; decisions live in <see cref="GatePlanner"/>.
/// Domain errors become ProblemDetails.
/// </summary>
[ApiController]
[Route("api/gates")]
public sealed class GatesController(IGatePlanStore store, TimeProvider time) : ControllerBase
{
    /// <summary>Lightweight hello for the gate-allocation domain — proves the API is up.</summary>
    [HttpGet("ping")]
    [ProducesResponseType(typeof(GatePingResponse), StatusCodes.Status200OK)]
    public ActionResult<GatePingResponse> Ping() =>
        Ok(new GatePingResponse(
            Service: "AeroFlow.GateAllocation",
            Message: "Gate allocation probe OK",
            UtcNow: time.GetUtcNow()));

    /// <summary>All gates, ordered by id, each with its allocations.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GateResponse>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<GateResponse>> GetAll()
    {
        var plan = store.Current();
        return Ok(plan.Gates.Values
            .OrderBy(g => g.Id.Value, StringComparer.Ordinal)
            .Select(g => GateResponse.From(plan, g))
            .ToList());
    }

    [HttpGet("{gate}")]
    [ProducesResponseType(typeof(GateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<GateResponse> GetById(string gate)
    {
        var plan = store.Current();
        return GateId.Parse(gate)
            .Bind(id => plan.FindGate(id).ToResult(() => new GateNotFound(id)))
            .Match<ActionResult>(g => Ok(GateResponse.From(plan, g)), this.Problem);
    }

    /// <summary>Takes a gate out of service. Rejected while it still has allocations.</summary>
    [HttpPost("{gate}/close")]
    [ProducesResponseType(typeof(GateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<GateResponse> Close(string gate, CloseGateRequest request) =>
        GateId.Parse(gate).Bind(id =>
        Fields.RequireText(request.Reason, "reason").Map(reason =>
            new CloseGate(id, reason)))
        .Match<ActionResult>(command => Run(command, command.Gate), this.Problem);

    [HttpPost("{gate}/reopen")]
    [ProducesResponseType(typeof(GateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<GateResponse> Reopen(string gate) =>
        GateId.Parse(gate)
            .Match<ActionResult>(id => Run(new ReopenGate(id), id), this.Problem);

    private ActionResult Run(GateCommand command, GateId gate) =>
        store.Update(plan => GatePlanner.Apply(plan, command))
            .Bind(plan => plan.FindGate(gate).ToResult(() => new GateNotFound(gate)).Map(g => GateResponse.From(plan, g)))
            .Match<ActionResult>(Ok, this.Problem);
}
