using System.Text.Json;
using System.Text.Json.Serialization;

namespace BashPrinterAgent;

/// <summary>
/// Read from appsettings.json sitting next to the exe, so a shop can be re-pointed by
/// editing one file rather than by shipping a new build.
/// </summary>
public sealed class AgentConfig
{
    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; set; } = "";

    [JsonPropertyName("agentKey")]
    public string AgentKey { get; set; } = "";

    /// <summary>
    /// Dots per inch of the print head. 203 on every common 80mm thermal unit, Remo
    /// included; 300 exists on some label printers. The server renders the receipt in dots,
    /// and this is what turns those back into a physical width.
    /// </summary>
    [JsonPropertyName("printerDpi")]
    public int PrinterDpi { get; set; } = 203;

    [JsonPropertyName("pollIntervalMs")]
    public int PollIntervalMs { get; set; } = 3000;

    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AgentConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            throw new FileNotFoundException(
                $"appsettings.json not found next to the exe.\n" +
                $"Copy appsettings.example.json to appsettings.json and fill in apiBaseUrl and agentKey.\n" +
                $"Expected at: {ConfigPath}");
        }

        var config = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(ConfigPath))
            ?? throw new InvalidOperationException("appsettings.json could not be parsed.");

        if (string.IsNullOrWhiteSpace(config.ApiBaseUrl))
            throw new InvalidOperationException("apiBaseUrl is empty in appsettings.json.");

        if (string.IsNullOrWhiteSpace(config.AgentKey))
            throw new InvalidOperationException("agentKey is empty in appsettings.json. Pair the machine in the admin panel to get one.");

        config.ApiBaseUrl = config.ApiBaseUrl.TrimEnd('/');

        if (config.PrinterDpi <= 0) config.PrinterDpi = 203;
        if (config.PollIntervalMs < 500) config.PollIntervalMs = 500;

        return config;
    }
}
