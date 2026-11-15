using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Google.Protobuf;

using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nefarius.Utilities.ETW.Relay.CLI;

/// <summary>
///     Posts one serialized OTLP request, retrying what is worth retrying. The request bytes are built once and sent
///     unchanged on every attempt, so a retry is byte-identical to the original and the gateway can recognise it.
/// </summary>
internal sealed class OtlpSender : IDisposable
{
    private const int ErrorBodyChars = 200;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly HttpClient _http;
    private readonly TextWriter _log;
    private readonly RelaySettings _settings;

    public OtlpSender(RelaySettings settings, TextWriter log, HttpMessageHandler? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _settings = settings;
        _log = log;
        _delay = delay ?? Task.Delay;
        // The per-request timeout is applied with a token so that it can be told apart from a shutdown.
        _http = handler is null ? new HttpClient() : new HttpClient(handler, false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Serializes (and optionally compresses) a request into the bytes that go on the wire.</summary>
    public byte[] Serialize(ExportLogsServiceRequest request)
    {
        byte[] raw = request.ToByteArray();
        if (!_settings.Gzip)
        {
            return raw;
        }

        using MemoryStream compressed = new();
        using (GZipStream gzip = new(compressed, CompressionLevel.Fastest, true))
        {
            gzip.Write(raw);
        }

        return compressed.ToArray();
    }

    /// <exception cref="DeliveryException">The batch was rejected or could not be delivered.</exception>
    public async Task SendAsync(byte[] body, CancellationToken cancellationToken)
    {
        for (int attempt = 0;; attempt++)
        {
            TimeSpan? retryAfter = null;
            string failure;

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_settings.RequestTimeout);

            try
            {
                using HttpRequestMessage request = CreateRequest(body);
                using HttpResponseMessage response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                    .ConfigureAwait(false);

                int status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    CheckPartialSuccess(await response.Content.ReadAsByteArrayAsync(timeout.Token)
                        .ConfigureAwait(false));
                    return;
                }

                if (status is 408 or 429 or >= 500)
                {
                    retryAfter = ReadRetryAfter(response.Headers.RetryAfter);
                    failure = $"gateway answered {status} {response.ReasonPhrase}";
                }
                else
                {
                    string text = await ReadBodySnippetAsync(response, timeout.Token).ConfigureAwait(false);
                    throw new DeliveryException(
                        $"The gateway rejected the request with {status} {response.ReasonPhrase}" +
                        (text.Length > 0 ? $": {text}" : ".") + " This is not retried.");
                }
            }
            catch (HttpRequestException ex)
            {
                failure = $"gateway unreachable ({ex.Message})";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failure = $"request timed out after {_settings.RequestTimeout.TotalSeconds:0.#}s";
            }

            if (attempt >= _settings.MaxRetries)
            {
                throw new DeliveryException($"Giving up after {attempt + 1} attempts: {failure}.");
            }

            TimeSpan wait = Backoff(attempt);
            if (retryAfter is { } requested)
            {
                wait = requested > wait ? requested : wait;
            }

            _log.WriteLine(
                $"[*] {failure}; retrying in {wait.TotalSeconds:0.##}s (attempt {attempt + 2} of {_settings.MaxRetries + 1}).");
            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private HttpRequestMessage CreateRequest(byte[] body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, _settings.Endpoint)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        if (_settings.Gzip)
        {
            request.Content.Headers.ContentEncoding.Add("gzip");
        }

        foreach (KeyValuePair<string, string> header in _settings.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value) &&
                !request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                throw new UsageException($"The header '{header.Key}' cannot be sent.");
            }
        }

        return request;
    }

    private TimeSpan Backoff(int attempt)
    {
        double ms = _settings.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, Math.Min(attempt, 20));
        return TimeSpan.FromMilliseconds(Math.Min(ms, _settings.RetryMaxDelay.TotalMilliseconds));
    }

    private TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? header)
    {
        TimeSpan? value = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (value is null)
        {
            return null;
        }

        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > _settings.RetryAfterCap ? _settings.RetryAfterCap : value;
    }

    /// <summary>
    ///     A 2xx can still carry rejected records, e.g. an unknown session. Resending would be rejected again, so this
    ///     is final.
    /// </summary>
    private static void CheckPartialSuccess(byte[] body)
    {
        if (body.Length == 0)
        {
            return;
        }

        ExportLogsServiceResponse response;
        try
        {
            response = ExportLogsServiceResponse.Parser.ParseFrom(body);
        }
        catch (InvalidProtocolBufferException)
        {
            // Some collectors answer with an unrelated body. The status already said the data arrived.
            return;
        }

        if (response.PartialSuccess is { RejectedLogRecords: > 0 } rejected)
        {
            string message = string.IsNullOrWhiteSpace(rejected.ErrorMessage)
                ? "no reason given"
                : Sanitize(rejected.ErrorMessage);
            throw new DeliveryException(
                $"The gateway rejected {rejected.RejectedLogRecords} log record(s): {message}. This is not retried.");
        }
    }

    private static async Task<string> ReadBodySnippetAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            return Sanitize(Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, ErrorBodyChars * 4)));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>Makes remote text safe to print: no control characters, bounded length.</summary>
    private static string Sanitize(string text)
    {
        StringBuilder sb = new(Math.Min(text.Length, ErrorBodyChars));
        foreach (char c in text)
        {
            if (sb.Length >= ErrorBodyChars)
            {
                sb.Append('…');
                break;
            }

            sb.Append(char.IsControl(c) ? ' ' : c);
        }

        return sb.ToString().Trim();
    }
}
