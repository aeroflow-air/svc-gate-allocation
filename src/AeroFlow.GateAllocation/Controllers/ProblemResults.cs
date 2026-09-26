using AeroFlow.GateAllocation.Domain;
using Microsoft.AspNetCore.Mvc;

namespace AeroFlow.GateAllocation.Controllers;

/// <summary>Maps a domain <see cref="Error"/> to ProblemDetails, with the error code as a <c>code</c> extension.</summary>
internal static class ProblemResults
{
    public static ObjectResult Problem(this ControllerBase controller, Error error)
    {
        var (status, title) = error switch
        {
            ValidationError => (StatusCodes.Status400BadRequest, "Invalid request"),
            NotFoundError => (StatusCodes.Status404NotFound, "Not found"),
            RuleViolation => (StatusCodes.Status409Conflict, "Gate allocation rejected"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            statusCode: status,
            title: title,
            detail: error.Message,
            instance: controller.HttpContext.Request.Path);
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem) { StatusCode = status };
    }
}
