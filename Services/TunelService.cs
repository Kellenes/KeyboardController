using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KeyboardController.Services;

public class TunnelService
{
    private Process? _tunnelProcess;

    public async Task<string?> StartAsync(int localPort)
    {
        var tcs = new TaskCompletionSource<string?>();

        var psi = new ProcessStartInfo
        {
            FileName = "ssh",
            Arguments = $"-o StrictHostKeyChecking=no -o ServerAliveInterval=15 -R 80:127.0.0.1:{localPort} nokey@localhost.run",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _tunnelProcess = new Process { StartInfo = psi };

        void HandleLog(string? line, string streamName)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            // Logging SSH output to console with color
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"[SSH {streamName}]: {line}");
            Console.ResetColor();

            // Looking for the URL in the output
            var match = Regex.Match(line, @"https://[a-zA-Z0-9-]+\.(lhr\.life|lhr\.rocks)");
            if (match.Success && !tcs.Task.IsCompleted)
            {
                tcs.TrySetResult(match.Value);
            }
        }

        _tunnelProcess.OutputDataReceived += (_, e) => HandleLog(e.Data, "OUT");
        _tunnelProcess.Start();
        _tunnelProcess.BeginOutputReadLine();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();

        // Wait for either the URL to be found or a timeout of 15 seconds
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        if (completed == tcs.Task)
        {
            return await tcs.Task;
        }

        return null;
    }

    private void Stop()
    {
        if (_tunnelProcess is { HasExited: false })
        {
            try
            {
                _tunnelProcess.Kill(true);
                _tunnelProcess.Dispose();
            }
            catch { }
        }
    }
}