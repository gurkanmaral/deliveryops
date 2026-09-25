using DeliveryOps.BuildingBlocks.Application;
using Microsoft.AspNetCore.Mvc;

namespace DeliveryOps.Core.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<T> FromResult<T>(Result<T> result) => result.IsSuccess
        ? Ok(result.Value)
        : ErrorResult<T>(result.Error);

    protected ActionResult FromResult(Result result) => result.IsSuccess
        ? NoContent()
        : ErrorResult(result.Error);

    private ActionResult<T> ErrorResult<T>(Error error)
    {
        ObjectResult result = ErrorResult(error);
        return result;
    }

    private ObjectResult ErrorResult(Error error)
    {
        int status = error.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "forbidden" => StatusCodes.Status403Forbidden,
            "validation" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict
        };
        return StatusCode(status, new ProblemDetails { Title = error.Code, Detail = error.Message, Status = status });
    }
}
