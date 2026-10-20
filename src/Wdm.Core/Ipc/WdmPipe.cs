using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Wdm.Core.Ipc;

/// <summary>
/// Per-user named pipe used to hand downloads to the already-running WDM window.
/// One JSON document per connection.
/// </summary>
public static class WdmPipe
{
    public static readonly string Name = $"WDM.Ipc.{Environment.UserName}";

    private const int MaxMessageBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Sends a request to the running instance. Returns false if none is listening.</summary>
    public static async Task<bool> TrySendAsync(ExternalDownloadRequest request, int timeoutMs = 1500)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeoutMs);
            var payload = JsonSerializer.SerializeToUtf8Bytes(request, Json);
            await client.WriteAsync(payload);
            await client.FlushAsync();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Accepts requests until cancelled, invoking <paramref name="onRequest"/> for each.</summary>
    public static async Task ListenAsync(Action<ExternalDownloadRequest> onRequest, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct);

                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int read;
                while ((read = await server.ReadAsync(chunk, ct)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaxMessageBytes) throw new InvalidDataException("IPC message too large.");
                }

                var request = JsonSerializer.Deserialize<ExternalDownloadRequest>(Encoding.UTF8.GetString(buffer.ToArray()), Json);
                if (request is { IsEmpty: false } or { ActivateOnly: true })
                {
                    onRequest(request);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                // Malformed or aborted client: drop it and keep serving (brief pause so a persistent failure cannot spin)
                try { await Task.Delay(100, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
