using System.Net;
using System.Text.Json;

namespace TutorScheduler.API.Middleware;

// Catches anything that escapes every Controller/Service - those already
// return clean 400/404/409 results via ApiControllerBase.HandleFailure, so
// reaching here means a genuine bug or infrastructure failure (e.g. the
// database being unreachable). Never leaks exception detail outside Development.
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var id)
                ? id?.ToString()
                : null;

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var payload = new
            {
                error = "An unexpected error occurred.",
                correlationId,
                detail = _environment.IsDevelopment() ? ex.Message : null
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
        }
    }
}
