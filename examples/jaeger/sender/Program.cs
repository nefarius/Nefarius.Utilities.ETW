using System.Net.Http.Headers;
using System.Text;

using Google.Protobuf;

using Nefarius.Utilities.ETW.OpenTelemetry;
using Nefarius.Utilities.ETW.Relay;

using OpenTelemetry.Proto.Collector.Logs.V1;

// A stand-in for a native capture client: it produces raw relay frames (no symbols, no decoding) and ships
// them as OTLP Logs to the decoding gateway. A real client copies EVENT_RECORDs from ProcessTrace instead.
string gateway = Environment.GetEnvironmentVariable("GATEWAY_URL") ?? "http://localhost:4318";
int intervalMs = int.Parse(Environment.GetEnvironmentVariable("INTERVAL_MS") ?? "1000");
int total = int.Parse(Environment.GetEnvironmentVariable("EVENT_COUNT") ?? "0"); // 0 = run until stopped

using HttpClient http = new() { BaseAddress = new Uri(gateway) };
// No shutdown source is wired up. A cancelled token here is a real shutdown and must not be retried as a timeout.
CancellationToken shutdown = CancellationToken.None;
Guid provider = new("6f3e0f19-6e4c-4a52-8a6b-9a1d0c2b7e11");
string[] names = ["Alice", "Bob", "Carol"];
int sent = 0;

while (total == 0 || sent < total)
{
    Guid session = Guid.NewGuid();
    ulong sequence = 0;
    Console.WriteLine($"Starting relay session {session}");

    List<byte[]> frames = [HelloFrame(session, sequence++)];
    RawEtwRecord? pending = null;

    bool ok = true;
    while (ok && (total == 0 || sent < total))
    {
        // The event stays pending until the gateway accepts it, so a retry resends the same record.
        RawEtwRecord record = pending ??
            SampleRecord(provider, (uint)sent, names[sent % names.Length], (byte)(sent % 7 == 6 ? 2 : 4));
        pending = record;
        if (frames.Count == 0 || !IsEventFrame(frames[^1]))
        {
            frames.Add(EventFrame(sequence++, record));
        }

        bool delivered = false;
        while (!delivered && ok)
        {
            ExportLogsServiceRequest request = RelayLogRecords.CreateRequest(session,
                frames.Select(f => (ReadOnlyMemory<byte>)f),
                [new("service.name", "relay-sample-sender"), new("host.name", Environment.MachineName)]);

            try
            {
                using ByteArrayContent content = new(request.ToByteArray());
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
                using HttpResponseMessage response = await http.PostAsync("/v1/logs", content, shutdown);

                if (!response.IsSuccessStatusCode)
                {
                    // 503 means "queue full, resend the identical request".
                    Console.WriteLine($"Gateway answered {(int)response.StatusCode}; retrying.");
                    await Task.Delay(intervalMs, shutdown);
                    continue;
                }

                ExportLogsServiceResponse body =
                    ExportLogsServiceResponse.Parser.ParseFrom(await response.Content.ReadAsByteArrayAsync(shutdown));
                if (body.PartialSuccess is { RejectedLogRecords: > 0 } rejected)
                {
                    if (rejected.ErrorMessage.Contains("session is unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        // The gateway forgot the session. Open another one and send this same event again.
                        Console.WriteLine("Session unknown; opening a replacement session and retrying the event.");
                        session = Guid.NewGuid();
                        sequence = 0;
                        frames = [HelloFrame(session, sequence++), EventFrame(sequence++, record)];
                        continue;
                    }

                    // Any other rejection abandons the event and starts a new session, as before.
                    Console.WriteLine($"Rejected: {rejected.ErrorMessage}. Starting a new session.");
                    sent++;
                    pending = null;
                    frames.Clear();
                    ok = false;
                    delivered = true;
                    continue;
                }

                sent++;
                pending = null;
                frames.Clear();
                delivered = true;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Gateway unreachable ({ex.Message}); retrying.");
                await Task.Delay(intervalMs, shutdown);
            }
            catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
            {
                Console.WriteLine("Gateway request timed out; retrying.");
                await Task.Delay(intervalMs, shutdown);
            }
        }

        if (ok)
        {
            await Task.Delay(intervalMs, shutdown);
        }
    }
}
Console.WriteLine($"Sent {sent} events.");
return;

static byte[] HelloFrame(Guid session, ulong sequence) => RawEtwFrameCodec.Encode(new RawEtwFrame
{
    Type = RawEtwFrameType.Hello,
    Sequence = sequence,
    Hello = new RawEtwHello { SessionId = session, MaxFrameLength = 1 << 20, Producer = "relay-sample-sender" }
});

static byte[] EventFrame(ulong sequence, RawEtwRecord record) => RawEtwFrameCodec.Encode(new RawEtwFrame
{
    Type = RawEtwFrameType.Event,
    Sequence = sequence,
    Record = record
});

static bool IsEventFrame(byte[] frame) =>
    frame.Length >= 8 &&
    System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6)) == (ushort)RawEtwFrameType.Event;

static RawEtwRecord SampleRecord(Guid provider, uint count, string name, byte level)
{
    RawEtwRecord record = new()
    {
        Flags = 0x40,
        ThreadId = 1234,
        ProcessId = 4321,
        TimeStamp = DateTime.UtcNow.ToFileTimeUtc(),
        ProviderId = provider,
        Level = level,
        Payload = [.. BitConverter.GetBytes(count), .. Encoding.Unicode.GetBytes(name + "\0")]
    };

    // TraceLogging metadata (extended data type 11): event "SampleEvent" with fields Count (u32) and Name (UTF-16 string)
    List<byte> body = [0, .. Encoding.UTF8.GetBytes("SampleEvent"), 0];
    body.AddRange([.. Encoding.UTF8.GetBytes("Count"), 0, 8]);
    body.AddRange([.. Encoding.UTF8.GetBytes("Name"), 0, 1]);
    record.ExtendedData.Add(new RawEtwExtendedData
    {
        ExtType = 11,
        Data = [.. BitConverter.GetBytes((ushort)(body.Count + 2)), .. body]
    });
    return record;
}