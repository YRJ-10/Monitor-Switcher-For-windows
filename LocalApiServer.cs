using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MonitorSwitcher;

public sealed class LocalApiServer : IDisposable
{
    private const int Port = 47777;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TcpListener listener = new(IPAddress.Loopback, Port);
    private Task? listenTask;

    public void Start()
    {
        listener.Start();
        listenTask = Task.Run(() => ListenLoopAsync(cancellation.Token));
        Log("Local API listening on http://127.0.0.1:47777/");
    }

    public void Dispose()
    {
        cancellation.Cancel();
        listener.Stop();
        try
        {
            listenTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // The listener is expected to throw when stopped.
        }
        cancellation.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Accept failed: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            await HandleRequestAsync(client, token);
        }
    }

    private async Task HandleRequestAsync(TcpClient client, CancellationToken token)
    {
        NetworkStream? stream = null;
        try
        {
            stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.ASCII, leaveOpen: true);

            string? requestLine = await reader.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                await WriteJsonAsync(stream, HttpStatusCode.BadRequest, new { ok = false, error = "Empty request" }, token);
                return;
            }

            string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await WriteJsonAsync(stream, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid request" }, token);
                return;
            }

            string method = parts[0].ToUpperInvariant();
            string path = parts[1].Split('?', 2)[0];

            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(token)))
            {
                // Drain headers. The API does not need a request body.
            }

            if (method != "GET" && method != "POST")
            {
                await WriteJsonAsync(stream, HttpStatusCode.MethodNotAllowed, new { ok = false, error = "Only GET and POST are supported" }, token);
                return;
            }

            if (!TryGetProfileId(path, out string profileId))
            {
                await WriteJsonAsync(stream, HttpStatusCode.NotFound, new { ok = false, error = "Use /profile/{id}" }, token);
                return;
            }

            Log($"{method} /profile/{profileId}");
            string profileFile = ResolveProfileFile(profileId);
            DisplayManager.LoadProfile(profileFile);
            await WriteJsonAsync(stream, HttpStatusCode.OK, new { ok = true, profile = profileId }, token);
        }
        catch (Exception ex)
        {
            Log($"Request failed: {ex.Message}");
            try
            {
                if (stream != null)
                {
                    await WriteJsonAsync(stream, HttpStatusCode.InternalServerError, new { ok = false, error = ex.Message }, token);
                }
            }
            catch
            {
                // The client may have disconnected.
            }
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private static bool TryGetProfileId(string path, out string profileId)
    {
        profileId = string.Empty;
        string[] segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 || !segments[0].Equals("profile", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(segments[1]))
        {
            return false;
        }

        profileId = Uri.UnescapeDataString(segments[1]);
        return true;
    }

    private static string ResolveProfileFile(string profileId)
    {
        return ProfileCatalog.ResolveProfileFile(profileId, AppContext.BaseDirectory);
    }

    private static async Task WriteJsonAsync(NetworkStream stream, HttpStatusCode statusCode, object response, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(response);
        string headers =
            $"HTTP/1.1 {(int)statusCode} {statusCode}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n" +
            "Access-Control-Allow-Origin: http://127.0.0.1\r\n" +
            "\r\n";

        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, token);
        await stream.WriteAsync(body, token);
    }

    private static void Log(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
        string logPath = Path.Combine(AppContext.BaseDirectory, "MonitorSwitcher.api.log");
        File.AppendAllText(logPath, line);
    }
}
