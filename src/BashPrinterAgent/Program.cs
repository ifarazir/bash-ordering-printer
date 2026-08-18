using System.Reflection;

namespace BashPrinterAgent;

public static class Program
{
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Contains("--version"))
        {
            Console.WriteLine(Version);
            return 0;
        }

        if (args.Contains("--list-printers"))
        {
            foreach (var name in ReceiptPrinter.InstalledPrinters())
                Console.WriteLine(name);

            return 0;
        }

        AgentConfig config;
        try
        {
            config = AgentConfig.Load();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            Console.WriteLine("\nPress any key to close…");
            Console.ReadKey();
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        Log($"Bash printer agent {Version} — {config.ApiBaseUrl}");
        Log("Ctrl+C to stop.");

        using var api = new ApiClient(config);
        await RunAsync(api, config, cancellation.Token);

        Log("Stopped.");
        return 0;
    }

    private static async Task RunAsync(ApiClient api, AgentConfig config, CancellationToken ct)
    {
        var pollInterval = config.PollIntervalMs;
        string? lastReportedState = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var printers = ReceiptPrinter.InstalledPrinters();
                var beat = await api.HeartbeatAsync(printers, ct);

                if (beat is not null && beat.PollIntervalMs >= 500)
                    pollInterval = beat.PollIntervalMs;

                // Only speak when something changes. A line every three seconds turns the
                // window into noise nobody reads, which is where real errors go to hide.
                var state = beat?.SelectedPrinter is null
                    ? "connected — no receipt printer chosen in the panel yet"
                    : $"connected — printing to \"{beat.SelectedPrinter}\"";

                if (state != lastReportedState)
                {
                    Log(state);
                    lastReportedState = state;
                }

                var job = await api.NextJobAsync(ct);
                if (job is not null)
                    await PrintJobAsync(api, config, job, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var state = $"offline — {ex.Message}";
                if (state != lastReportedState)
                {
                    Log(state);
                    lastReportedState = state;
                }
            }

            try
            {
                await Task.Delay(pollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static async Task PrintJobAsync(ApiClient api, AgentConfig config, PrintJob job, CancellationToken ct)
    {
        Log($"Job #{job.Id}{(job.OrderId is null ? "" : $" (order #{job.OrderId})")} — {job.Copies.Count} copies to \"{job.Printer}\"");

        try
        {
            foreach (var copy in job.Copies)
            {
                var png = await api.DownloadCopyAsync(copy.ImageUrl, ct);
                ReceiptPrinter.Print(job.Printer, png, config.PrinterDpi);
                Log($"  printed {copy.Kind}");

                // The gap the brand asked for between the customer copy and the kitchen
                // one. It rides on the job rather than on this program's config, so the
                // number an operator set in the panel is the number that gets honoured.
                if (copy.DelayAfterMs > 0)
                    await Task.Delay(copy.DelayAfterMs, ct);
            }

            await api.CompleteAsync(job.Id, ok: true, error: null, ct);
            Log($"Job #{job.Id} done");
        }
        catch (Exception ex)
        {
            Log($"Job #{job.Id} failed: {ex.Message}");

            // Reported even when it fails, so the panel shows why rather than leaving the
            // job stuck at "printing" with nobody the wiser.
            try
            {
                await api.CompleteAsync(job.Id, ok: false, error: ex.Message, ct);
            }
            catch (Exception reportFailure)
            {
                Log($"  could not report the failure: {reportFailure.Message}");
            }
        }
    }

    private static void Log(string message) =>
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}
