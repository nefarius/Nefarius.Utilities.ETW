using Nefarius.Utilities.ETW.OtlpGateway;

using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddEtwOtlpGateway(builder.Configuration);

// The gateway ships without an authentication scheme on purpose: register the one your deployment uses here
// (JWT bearer, mutual TLS, ...) and set Gateway:RequireAuthorization, or run behind an authenticating proxy or
// OpenTelemetry Collector.
GatewayOptions gatewayOptions = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>() ??
                                new GatewayOptions();
if (gatewayOptions.RequireAuthorization)
{
    builder.Services.AddAuthorization();
}

builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Limits.MaxRequestBodySize = gatewayOptions.Limits.MaxRequestBodyBytes);

WebApplication app = builder.Build();

app.UseSerilogRequestLogging(options => options.GetLevel = (httpContext, _, exception) =>
    GatewayRequestLogging.GetLevel(httpContext, exception));

app.Services.GetRequiredService<GatewayMetrics>().Observe(
    () => app.Services.GetRequiredService<Nefarius.Utilities.ETW.OpenTelemetry.RelayLogProcessor>().ActiveSessions,
    () => app.Services.GetRequiredService<DecodedLogQueue>().Count);

if (gatewayOptions.RequireAuthorization)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

IEndpointConventionBuilder otlp = app.MapEtwOtlpGateway();
if (gatewayOptions.RequireAuthorization)
{
    otlp.RequireAuthorization();
}

app.MapHealthChecks("/healthz");

app.Run();

/// <summary>Entry point type, public so integration tests can host the gateway.</summary>
public partial class Program;
