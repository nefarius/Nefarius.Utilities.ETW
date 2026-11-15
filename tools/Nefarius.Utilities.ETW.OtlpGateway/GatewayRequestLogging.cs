using Serilog.Events;

namespace Nefarius.Utilities.ETW.OtlpGateway;

/// <summary>
///     Selects the level of one HTTP request completion event.
/// </summary>
internal static class GatewayRequestLogging
{
    /// <summary>
    ///     Successful health checks stay below the shipped Information minimum. Their warnings and errors still surface,
    ///     as do ordinary requests.
    /// </summary>
    public static LogEventLevel GetLevel(HttpContext httpContext, Exception? exception)
    {
        if (exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return LogEventLevel.Warning;
        }

        if (httpContext.Request.Path.StartsWithSegments("/healthz"))
        {
            return LogEventLevel.Verbose;
        }

        return LogEventLevel.Information;
    }
}
