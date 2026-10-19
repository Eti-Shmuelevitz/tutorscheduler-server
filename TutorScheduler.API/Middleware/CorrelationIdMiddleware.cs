namespace TutorScheduler.API.Middleware;

// Generates (or forwards) a correlation id for every request, echoes it back
// as a response header regardless of status code, and pushes it into the
// logging scope via the abstract ILogger.BeginScope - not an NLog-specific
// API - so this middleware doesn't need to know NLog is what's underneath.
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing)
            && !string.IsNullOrWhiteSpace(existing)
                ? existing.ToString()
                : Guid.NewGuid().ToString();

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            _logger.LogInformation("Incoming request {Method} {Path}", context.Request.Method, context.Request.Path);
            await _next(context);
        }
    }
}
