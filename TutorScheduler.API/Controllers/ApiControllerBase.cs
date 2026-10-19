using Microsoft.AspNetCore.Mvc;
using TutorScheduler.Core.Common;

namespace TutorScheduler.API.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    // Only the failure statuses: the success path varies per action (200 vs
    // 201-with-Location vs 204), so each action builds that response itself.
    protected ActionResult HandleFailure(ResultStatus status, string? error) => status switch
    {
        ResultStatus.NotFound => NotFound(new { error }),
        ResultStatus.Conflict => Conflict(new { error }),
        ResultStatus.ValidationError => BadRequest(new { error }),
        ResultStatus.Unauthorized => Unauthorized(new { error }),
        // StatusCode, not Forbid(): Forbid() hands off to the auth handler and
        // returns an empty 403, losing the error message.
        ResultStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error }),
        _ => StatusCode(500, new { error })
    };
}
