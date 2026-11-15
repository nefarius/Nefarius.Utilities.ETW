using System.IO.Compression;
using System.Security.Claims;
using System.Text;

using Google.Protobuf;

using Grpc.Core;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

using Nefarius.Utilities.ETW.Deserializer.WPP;
using Nefarius.Utilities.ETW.OpenTelemetry;
using Nefarius.Utilities.ETW.Relay;

using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nefarius.Utilities.ETW.OtlpGateway;

/// <summary>
///     Determines which tenant an authenticated request belongs to. Relay sessions are isolated per tenant.
/// </summary>
public interface IGatewayTenantResolver
{
    string Resolve(HttpContext context);
}

/// <summary>
///     Uses the authenticated user's name or <c>sub</c> claim; unauthenticated requests share one anonymous tenant.
/// </summary>
public sealed class DefaultTenantResolver : IGatewayTenantResolver
{
    public string Resolve(HttpContext context)
    {
        ClaimsPrincipal user = context.User;
        if (user.Identity is { IsAuthenticated: true })
        {
            return user.Identity.Name ?? user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                "authenticated";
        }

        return "anonymous";
    }
}

/// <summary>
///     The outcome of handling one export request.
/// </summary>
public readonly record struct GatewayOutcome(bool Retry, ExportLogsServiceResponse Response);

/// <summary>
///     Receive, decode, enqueue: shared by the HTTP and gRPC front ends.
/// </summary>
public sealed class GatewayPipeline(
    RelayLogProcessor processor,
    GatewaySymbolResolver symbols,
    DecodedLogQueue queue,
    GatewayMetrics metrics)
{
    public async ValueTask<GatewayOutcome> HandleAsync(string tenant, ExportLogsServiceRequest request,
        CancellationToken cancellationToken)
    {
        // The reservation is exclusive, so concurrent requests cannot consume frames against one free slot.
        // It is released when the request produces no batch; otherwise it stays with the queued batch.
        if (!queue.TryReserve())
        {
            metrics.RequestsRefused.Add(1);
            return new GatewayOutcome(true, new ExportLogsServiceResponse());
        }

        bool queued = false;
        try
        {
            // Best effort: symbol problems never fail the request, and later requests see what loads now.
            await symbols.DiscoverAsync(request, cancellationToken).ConfigureAwait(false);

            RelayLogResult result = processor.Process(tenant, request);

            metrics.FramesAccepted.Add(result.Accepted);
            metrics.FramesRejected.Add(result.Rejected);
            if (result.LostEvents > 0)
            {
                metrics.EventsLost.Add((long)Math.Min(result.LostEvents, long.MaxValue));
            }

            if (result.Decoded.ResourceLogs.Count > 0)
            {
                long events = result.Decoded.ResourceLogs.Sum(r => r.ScopeLogs.Sum(s => (long)s.LogRecords.Count));
                metrics.EventsDecoded.Add(events);
                // Ignore the caller's cancellation: the frames are already consumed, and the slot was reserved.
                queue.EnqueueReserved(new DecodedBatch(tenant, result.Decoded));
                queued = true;
            }

            return new GatewayOutcome(false, result.ToResponse());
        }
        finally
        {
            if (!queued)
            {
                queue.Release();
            }
        }
    }
}

/// <summary>
///     The gRPC front end (OTLP/gRPC <c>LogsService.Export</c>).
/// </summary>
public sealed class OtlpGrpcLogsService(GatewayPipeline pipeline, IGatewayTenantResolver tenants)
    : LogsService.LogsServiceBase
{
    public override async Task<ExportLogsServiceResponse> Export(ExportLogsServiceRequest request,
        ServerCallContext context)
    {
        HttpContext http = context.GetHttpContext();
        GatewayOutcome outcome = await pipeline.HandleAsync(tenants.Resolve(http), request, context.CancellationToken)
            .ConfigureAwait(false);

        if (outcome.Retry)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "The export queue is full; retry later."));
        }

        return outcome.Response;
    }
}

/// <summary>
///     Registration and endpoint mapping. Authentication and authorization are the host's business: call these from
///     any ASP.NET application, register the authentication scheme of your choice, and apply policies to the
///     returned builder.
/// </summary>
public static class GatewayExtensions
{
    public static IServiceCollection AddEtwOtlpGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GatewayOptions>(configuration.GetSection(GatewayOptions.SectionName));

        services.AddSingleton<GatewayMetrics>();
        services.AddSingleton<IGatewayTenantResolver, DefaultTenantResolver>();
        services.AddSingleton<DecodedLogQueue>();
        services.AddSingleton<GatewayPipeline>();
        services.AddSingleton(provider =>
        {
            GatewayOptions options = provider.GetRequiredService<IOptions<GatewayOptions>>().Value;
            ILoggerFactory loggers = provider.GetRequiredService<ILoggerFactory>();
            DecodingContext? wpp = WppAssets.Load(options.Wpp, loggers.CreateLogger("WppAssets"));
            bool discovery = !string.IsNullOrWhiteSpace(options.Wpp.SymbolServer);

            // Discovery needs a context to extend, even when no local assets exist.
            wpp ??= discovery ? new DecodingContext(new List<DecodingContextType>()) : null;

            return new GatewayWppAssets(wpp);
        });
        services.AddSingleton(provider =>
        {
            GatewayOptions options = provider.GetRequiredService<IOptions<GatewayOptions>>().Value;
            return new GatewaySymbolResolver(
                options.Wpp,
                provider.GetRequiredService<GatewayWppAssets>().Context,
                string.IsNullOrWhiteSpace(options.Wpp.SymbolServer)
                    ? null
                    : provider.GetRequiredService<IHttpClientFactory>()
                        .CreateClient(GatewaySymbolResolver.HttpClientName),
                new RawEtwRelayLimits { MaxFrameLength = options.Limits.MaxFrameBytes },
                provider.GetRequiredService<ILoggerFactory>().CreateLogger("SymbolDiscovery"));
        });
        services.AddHttpClient(GatewaySymbolResolver.HttpClientName, (provider, client) =>
                client.Timeout = provider.GetRequiredService<IOptions<GatewayOptions>>().Value.Wpp.SymbolRequestTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddSingleton(provider =>
        {
            GatewayOptions options = provider.GetRequiredService<IOptions<GatewayOptions>>().Value;
            DecodingContext? wpp = provider.GetRequiredService<GatewayWppAssets>().Context;
            ILogger sessions = provider.GetRequiredService<ILoggerFactory>().CreateLogger("RelaySessions");

            return new RelayLogProcessor(new RelayLogProcessorOptions
            {
                DecodingOptions = o =>
                {
                    o.WppDecodingContext = wpp;
                    o.RewriteWppProviderName = options.Wpp.RewriteProviderName;
                },
                Limits = new RawEtwRelayLimits { MaxFrameLength = options.Limits.MaxFrameBytes },
                MaxSessions = options.Limits.MaxSessions,
                MaxSessionsPerTenant = options.Limits.MaxSessionsPerTenant,
                MaxRecordsPerRequest = options.Limits.MaxRecordsPerRequest,
                SessionIdleTimeout = options.Limits.SessionIdleTimeout,
                OnHello = (tenant, sessionId, hello) => sessions.LogInformation(
                    "Relay hello accepted for session {SessionId}, tenant {Tenant}, producer {Producer}, maximum frame size {MaxFrameLength}.",
                    sessionId, tenant, EscapeForLog(hello.Producer), hello.MaxFrameLength),
                OnEnd = (tenant, sessionId) => sessions.LogInformation(
                    "Relay end accepted for session {SessionId}, tenant {Tenant}.",
                    sessionId, tenant)
            });
        });

        services.AddHttpClient(ConfiguredSinks.HttpClientName, (provider, client) =>
            client.Timeout = provider.GetRequiredService<IOptions<GatewayOptions>>().Value.Export.RequestTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        // One evaluation of the export options. Host-added IDecodedLogSink registrations are separate and are
        // appended by the exporter.
        services.AddSingleton(provider =>
        {
            GatewayOptions.ExportOptions export = provider.GetRequiredService<IOptions<GatewayOptions>>().Value.Export;
            HttpClient client = provider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(ConfiguredSinks.HttpClientName);
            return new ConfiguredExportSinks(ConfiguredSinks.Create(export, client).ToArray());
        });

        services.AddHostedService<SymbolResolverStartup>();
        services.AddHostedService<DecodedLogExporter>();
        services.AddGrpc();
        services.AddHealthChecks();

        return services;
    }

    /// <summary>
    ///     Maps <c>POST /v1/logs</c> (OTLP/HTTP, protobuf) and the OTLP/gRPC logs service. The returned builder applies
    ///     conventions such as <c>RequireAuthorization()</c> to both.
    /// </summary>
    public static IEndpointConventionBuilder MapEtwOtlpGateway(this IEndpointRouteBuilder endpoints)
    {
        RouteHandlerBuilder http = endpoints.MapPost("/v1/logs", HandleHttpAsync)
            .WithName("OtlpLogs")
            .DisableAntiforgery();
        GrpcServiceEndpointConventionBuilder grpc = endpoints.MapGrpcService<OtlpGrpcLogsService>();
        return new CompositeConventionBuilder(http, grpc);
    }

    private static async Task<IResult> HandleHttpAsync(HttpContext context, GatewayPipeline pipeline,
        IGatewayTenantResolver tenants, IOptions<GatewayOptions> options)
    {
        HttpRequest request = context.Request;
        if (request.ContentType is null ||
            !request.ContentType.StartsWith("application/x-protobuf", StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        int limit = options.Value.Limits.MaxRequestBodyBytes;
        ExportLogsServiceRequest message;
        try
        {
            Stream body = request.Body;
            string? encoding = request.Headers.ContentEncoding.ToString();
            if (string.Equals(encoding, "gzip", StringComparison.OrdinalIgnoreCase))
            {
                body = new GZipStream(body, CompressionMode.Decompress, leaveOpen: true);
            }
            else if (!string.IsNullOrEmpty(encoding) && !string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase))
            {
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            }

            byte[]? bytes = await ReadLimitedAsync(body, limit, context.RequestAborted).ConfigureAwait(false);
            if (bytes is null)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            message = ExportLogsServiceRequest.Parser.ParseFrom(bytes);
        }
        catch (InvalidProtocolBufferException)
        {
            return Results.BadRequest("The request body is not a valid ExportLogsServiceRequest.");
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest("The request body is not valid gzip data.");
        }

        GatewayOutcome outcome = await pipeline.HandleAsync(tenants.Resolve(context), message, context.RequestAborted)
            .ConfigureAwait(false);

        if (outcome.Retry)
        {
            context.Response.Headers.RetryAfter = "5";
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Bytes(outcome.Response.ToByteArray(), "application/x-protobuf");
    }

    /// <summary>
    ///     Reads a stream up to <paramref name="limit" /> bytes; <see langword="null" /> when it is longer.
    /// </summary>
    private static async Task<byte[]?> ReadLimitedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    ///     Keeps untrusted text on one log line. Carriage returns, line feeds, and other terminal control characters are
    ///     escaped.
    /// </summary>
    private static string EscapeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder escaped = new(value.Length);
        foreach (char c in value)
        {
            if (char.IsControl(c) || c is '\u2028' or '\u2029')
            {
                escaped.Append($"\\u{(ushort)c:X4}");
            }
            else
            {
                escaped.Append(c);
            }
        }

        return escaped.ToString();
    }

    private sealed class CompositeConventionBuilder(params IEndpointConventionBuilder[] builders)
        : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention)
        {
            foreach (IEndpointConventionBuilder builder in builders)
            {
                builder.Add(convention);
            }
        }

        public void Finally(Action<EndpointBuilder> finallyConvention)
        {
            foreach (IEndpointConventionBuilder builder in builders)
            {
                builder.Finally(finallyConvention);
            }
        }
    }
}

/// <summary>The shared server-side WPP context, or <see langword="null" /> when no assets are configured.</summary>
public sealed record GatewayWppAssets(DecodingContext? Context);

/// <summary>
///     Builds the server-side WPP <see cref="DecodingContext" /> from the configured PDB and TMF assets.
/// </summary>
public static class WppAssets
{
    public static DecodingContext? Load(GatewayOptions.WppOptions options, ILogger logger)
    {
        List<string> pdbs = [.. options.PdbFiles];
        foreach (string directory in options.PdbDirectories)
        {
            if (Directory.Exists(directory))
            {
                pdbs.AddRange(Directory.EnumerateFiles(directory, "*.pdb"));
            }
            else
            {
                logger.LogWarning("The PDB directory {Directory} does not exist.", directory);
            }
        }

        List<DecodingContextType> types = [];
        List<string> existing = [];
        foreach (string pdb in pdbs)
        {
            if (File.Exists(pdb))
            {
                existing.Add(pdb);
            }
            else
            {
                logger.LogWarning("The PDB file {File} does not exist.", pdb);
            }
        }

        if (existing.Count > 0)
        {
            types.AddRange(PdbFileDecodingContextType.CreateFrom(existing));
        }

        foreach (string directory in options.TmfDirectories)
        {
            if (Directory.Exists(directory))
            {
                types.AddRange(TmfFilesDirectoryDecodingContextType.CreateFrom(directory));
            }
            else
            {
                logger.LogWarning("The TMF directory {Directory} does not exist.", directory);
            }
        }

        if (types.Count == 0)
        {
            logger.LogWarning("No WPP assets are configured; WPP events will carry the missing-format placeholder.");
            return null;
        }

        logger.LogInformation("Loaded WPP formats from {Count} source(s).", types.Count);
        return new DecodingContext(types);
    }
}
