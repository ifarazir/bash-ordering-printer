using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace BashPrinterAgent;

public sealed record PrintCopy(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("delay_after_ms")] int DelayAfterMs,
    [property: JsonPropertyName("image_url")] string ImageUrl);

public sealed record PrintJob(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("order_id")] long? OrderId,
    [property: JsonPropertyName("printer")] string Printer,
    [property: JsonPropertyName("copies")] List<PrintCopy> Copies);

public sealed record HeartbeatResult(
    [property: JsonPropertyName("selected_printer")] string? SelectedPrinter,
    [property: JsonPropertyName("selected_printers")] List<string>? SelectedPrinters,
    [property: JsonPropertyName("poll_interval_ms")] int PollIntervalMs,
    [property: JsonPropertyName("receipt_width_dots")] int ReceiptWidthDots);

/// <summary>
/// The only way this program talks to the outside world: outbound HTTPS, nothing listening.
/// A till on a shop network has no port to forward and no certificate to manage that way.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    public ApiClient(AgentConfig config)
    {
        _http = new HttpClient { BaseAddress = new Uri(config.ApiBaseUrl + "/") };
        _http.DefaultRequestHeaders.Add("X-Print-Agent-Key", config.AgentKey);
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    private sealed record Envelope<T>([property: JsonPropertyName("data")] T? Data);

    /// <summary>
    /// Announce the machine and its printers, and learn which one receipts go to. This is
    /// also what keeps the connection light in the POS lit.
    /// </summary>
    public async Task<HeartbeatResult?> HeartbeatAsync(IReadOnlyList<string> printers, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync("api/v1/print-agent/heartbeat", new
        {
            machine_name = Environment.MachineName,
            app_version = Program.Version,
            printers,
        }, ct);

        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<HeartbeatResult>>(cancellationToken: ct);
        return envelope?.Data;
    }

    /// <summary>Claims the next receipt, or null when the queue is empty (204).</summary>
    public async Task<PrintJob?> NextJobAsync(CancellationToken ct)
    {
        var response = await _http.GetAsync("api/v1/print-agent/jobs/next", ct);

        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;

        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<PrintJob>>(cancellationToken: ct);
        return envelope?.Data;
    }

    public async Task<byte[]> DownloadCopyAsync(string imageUrl, CancellationToken ct)
    {
        // Absolute, because the server builds it from its own APP_URL.
        var response = await _http.GetAsync(imageUrl, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task CompleteAsync(long jobId, bool ok, string? error, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(
            $"api/v1/print-agent/jobs/{jobId}/complete",
            new { ok, error },
            ct);

        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
