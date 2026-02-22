using System.Diagnostics;

namespace SmsReader.Adb;

public sealed class AdbResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string Error { get; init; } = "";
    public bool Success => ExitCode == 0 && !Error.Contains("error:", StringComparison.OrdinalIgnoreCase);
}

public sealed class AdbClient
{
    private readonly string _adbPath;
    private readonly string? _deviceSerial;

    public AdbClient(string adbPath, string? deviceSerial = null)
    {
        _adbPath = adbPath;
        _deviceSerial = deviceSerial;
    }

    public async Task<AdbResult> ExecuteAsync(string arguments, int timeoutMs = 10000)
    {
        var fullArgs = _deviceSerial != null
            ? $"-s {_deviceSerial} {arguments}"
            : arguments;

        var psi = new ProcessStartInfo
        {
            FileName = _adbPath,
            Arguments = fullArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };
        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            return new AdbResult
            {
                ExitCode = process.ExitCode,
                Output = await outputTask,
                Error = await errorTask
            };
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new AdbResult
            {
                ExitCode = -1,
                Error = $"error: ADB not found at '{_adbPath}'. {ex.Message}"
            };
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new AdbResult
            {
                ExitCode = -1,
                Error = $"Command timed out after {timeoutMs}ms: adb {fullArgs}"
            };
        }
    }

    public async Task<AdbResult> ExecuteAsync(string arguments, CancellationToken ct)
    {
        var fullArgs = _deviceSerial != null
            ? $"-s {_deviceSerial} {arguments}"
            : arguments;

        var psi = new ProcessStartInfo
        {
            FileName = _adbPath,
            Arguments = fullArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            return new AdbResult
            {
                ExitCode = process.ExitCode,
                Output = await outputTask,
                Error = await errorTask
            };
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return new AdbResult
            {
                ExitCode = -1,
                Error = $"error: ADB not found at '{_adbPath}'. {ex.Message}"
            };
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }
}
