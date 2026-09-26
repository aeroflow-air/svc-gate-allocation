using AeroFlow.GateAllocation.Domain;
using AeroFlow.GateAllocation.Models;
using AeroFlow.GateAllocation.Storage;
using Microsoft.AspNetCore.Mvc;

namespace AeroFlow.GateAllocation.Controllers;

/// <summary>
/// HTTP edge for flight-to-gate allocations. Each write parses the body into a <see cref="GateCommand"/>,
/// applies it to the stored plan through <see cref="GatePlanner"/>, and returns the flight's allocation.
/// </summary>
[ApiController]
[Route("api/allocations")]
public sealed class AllocationsController(IGatePlanStore store) : ControllerBase
{
    [HttpGet("{flightNumber}")]
    [ProducesResponseType(typeof(AllocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<AllocationResponse> GetByFlight(string flightNumber) =>
        FlightNumber.Parse(flightNumber)
            .Bind(flight => AllocationOf(store.Current(), flight))
            .Match<ActionResult>(a => Ok(AllocationResponse.From(a)), this.Problem);

    /// <summary>Allocates a gate to a flight. With no <c>gate</c> in the body the planner picks the best free one.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AllocationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<AllocationResponse> Allocate(AllocationRequest request) =>
        request.ToCommand()
            .Bind(command => Run(command, command.Turn.Flight))
            .Match<ActionResult>(
                a => CreatedAtAction(nameof(GetByFlight), new { flightNumber = a.Flight.Value }, AllocationResponse.From(a)),
                this.Problem);

    [HttpPut("{flightNumber}/gate")]
    [ProducesResponseType(typeof(AllocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<AllocationResponse> Reassign(string flightNumber, ReassignRequest request) =>
        FlightNumber.Parse(flightNumber).Bind(flight =>
        GateId.Parse(request.Gate).Bind(gate =>
            Run(new ReassignGate(flight, gate), flight)))
        .Match<ActionResult>(a => Ok(AllocationResponse.From(a)), this.Problem);

    /// <summary>Moves the on-block window (e.g. after a delay). The flight keeps its gate or the change is rejected.</summary>
    [HttpPut("{flightNumber}/window")]
    [ProducesResponseType(typeof(AllocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public ActionResult<AllocationResponse> Retime(string flightNumber, RetimeRequest request) =>
        FlightNumber.Parse(flightNumber).Bind(flight =>
        Fields.Window(request.OnBlock, request.OffBlock).Bind(window =>
            Run(new RetimeTurn(flight, window), flight)))
        .Match<ActionResult>(a => Ok(AllocationResponse.From(a)), this.Problem);

    [HttpDelete("{flightNumber}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult Release(string flightNumber) =>
        FlightNumber.Parse(flightNumber)
            .Bind(flight => store.Update(plan => GatePlanner.Apply(plan, new ReleaseGate(flight))))
            .Match<ActionResult>(_ => NoContent(), this.Problem);

    private Result<Allocation> Run(GateCommand command, FlightNumber flight) =>
        store.Update(plan => GatePlanner.Apply(plan, command))
            .Bind(plan => AllocationOf(plan, flight));

    private static Result<Allocation> AllocationOf(GatePlan plan, FlightNumber flight) =>
        plan.AllocationFor(flight).ToResult(() => new AllocationNotFound(flight));
}
