using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KeyboardController.Services;

public class TunnelService
{
    private Process? _tunnelProcess;

    public async Task<string?> StartAsync(int localPort)
    {
        var tcs = new TaskCompletionSource<string?>();

        // -v : подробный лог каждого сетевого пакета
        // -tt: отключение буферизации (вывод сразу идет в консоль)
        // -p 443: обход стандартных блокировок 22 порта
        var psi = new ProcessStartInfo
        {
            FileName = "ssh",
            // nokey@localhost.run - официальный публичный шлюз без паролей
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

            // Выводим сырой лог SSH в консоль желтым цветом, чтобы ты видел всё
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"[SSH {streamName}]: {line}");
            Console.ResetColor();

            // Ищем ссылку в потоке
            // localhost.run выдает адреса на домене lhr.life или lhr.rocks
            var match = Regex.Match(line, @"https://[a-zA-Z0-9-]+\.(lhr\.life|lhr\.rocks)");
            if (match.Success && !tcs.Task.IsCompleted)
            {
                tcs.TrySetResult(match.Value);
            }
        }

        _tunnelProcess.OutputDataReceived += (_, e) => HandleLog(e.Data, "OUT");

        _tunnelProcess.Start();

        // Запуск неблокирующего асинхронного чтения обоих потоков
        _tunnelProcess.BeginOutputReadLine();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();

        // Ждем либо поимки URL, либо таймаута 15 секунд
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        if (completed == tcs.Task)
        {
            return await tcs.Task;
        }

        return null;
    }

    public void Stop()
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