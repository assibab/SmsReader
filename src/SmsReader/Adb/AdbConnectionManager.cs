using SmsReader.Configuration;
using Spectre.Console;

namespace SmsReader.Adb;

public sealed class AdbConnectionManager
{
    private readonly AdbClient _client;
    private readonly AdbSettings _settings;
    private readonly string _deviceAddress;

    public AdbConnectionManager(AdbClient client, AdbSettings settings)
    {
        _client = client;
        _settings = settings;
        _deviceAddress = $"{settings.DeviceIp}:{settings.Port}";
    }

    public async Task<bool> EnsureConnectedAsync()
    {
        // First check if already connected
        if (await IsDeviceOnlineAsync())
            return true;

        // Try to connect
        AnsiConsole.MarkupLine($"[yellow]Connecting to {Markup.Escape(_deviceAddress)}...[/]");
        var result = await _client.ExecuteAsync(
            $"connect {_deviceAddress}",
            _settings.CommandTimeoutMs);

        if (result.Output.Contains("connected", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[green]Connected to {Markup.Escape(_deviceAddress)}[/]");
            return true;
        }

        AnsiConsole.MarkupLine($"[red]Failed to connect: {Markup.Escape(result.Output.Trim())} {Markup.Escape(result.Error.Trim())}[/]");
        return false;
    }

    public async Task<bool> IsDeviceOnlineAsync()
    {
        // Use a separate client without -s flag for 'devices' command
        var rawClient = new AdbClient(_settings.Path);
        var result = await rawClient.ExecuteAsync("devices", _settings.CommandTimeoutMs);

        if (!result.Success)
            return false;

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Contains(_deviceAddress) && line.Contains("device"));
    }

    public async Task<bool> ReconnectAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Device disconnected. Attempting to reconnect...[/]");

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            if (await EnsureConnectedAsync())
                return true;

            AnsiConsole.MarkupLine($"[grey]Retry {attempt}/3...[/]");
            await Task.Delay(2000);
        }

        AnsiConsole.MarkupLine("[red]Could not reconnect to device after 3 attempts.[/]");
        return false;
    }
}
