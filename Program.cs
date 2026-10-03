using InputSimulatorStandard;
using InputSimulatorStandard.Native;
using QRCoder;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

const int Port = 8181;
var builder = WebApplication.CreateBuilder(args);

// Слушаем локально на порту 8181
builder.WebHost.UseUrls($"http://localhost:{Port}");
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
var simulator = new InputSimulator();

// Раздача веб-страницы и сокетов
app.MapGet("/", () => Results.Content(GetHtmlPage(), "text/html", Encoding.UTF8));
app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        Console.WriteLine($"\n[+] Телефон подключился!");

        var buffer = new byte[1024 * 4];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    HandleClientCommand(json, simulator);
                }
            }
        }
        catch { }
        finally { Console.WriteLine("[-] Телефон отключился."); }
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

// Запускаем веб-сервер
await app.StartAsync();
Console.WriteLine($"[✓] Локальный сервер запущен на http://localhost:{Port}");

// Запуск туннеля через системный SSH
var tunnelProcess = StartPinggyTunnel(Port);

AppDomain.CurrentDomain.ProcessExit += (s, e) =>
{
    if (tunnelProcess is { HasExited: false }) tunnelProcess.Kill(true);
};

Console.WriteLine("Нажми Ctrl+C для выхода.");
await app.WaitForShutdownAsync();

// === ЛОГИКА ТУННЕЛЯ И ЗАГРУЗКИ ===
static Process StartPinggyTunnel(int port)
{
    Console.WriteLine("[*] Поднимаем публичный туннель через защищенный шлюз...");

    // Используем встроенный в Windows SSH-клиент (system32\OpenSSH\ssh.exe)
    var psi = new ProcessStartInfo
    {
        FileName = "ssh",
        // Подключаемся к бесплатному шлюзу pinggy на порту 443 (обходит любые блокировки провайдеров)
        // b:true заставляет pinggy вывести только URL
        Arguments = $"-p 443 -R0:localhost:{port} -o StrictHostKeyChecking=no -o ServerAliveInterval=30 a.pinggy.io",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    var process = new Process { StartInfo = psi };
    bool urlFound = false;

    void ProcessLine(string? line)
    {
        if (string.IsNullOrEmpty(line) || urlFound) return;

        // Ловим выданную ссылку вида https://xxxx.free.pinggy.link
        var match = Regex.Match(line, @"https://[a-zA-Z0-9-]+\.free\.pinggy\.link");
        if (match.Success)
        {
            urlFound = true;
            var url = match.Value;
            DisplayConnectionInfo(url);
        }
    }

    process.OutputDataReceived += (sender, args) => ProcessLine(args.Data);
    process.ErrorDataReceived += (sender, args) => ProcessLine(args.Data);

    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    return process;
}

static void DisplayConnectionInfo(string url)
{
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("==================================================");
    Console.WriteLine("          СЕРВЕР ГОТОВ К РАБОТЕ!                  ");
    Console.WriteLine("==================================================");
    Console.ResetColor();

    Console.WriteLine($"\nСсылка для телефона: \n--> {url}\n");
    Console.WriteLine("Или отсканируй QR-код камерой смартфона:\n");

    // Генерация ANSI QR-кода прямо в окно консоли
    using var qrGenerator = new QRCodeGenerator();
    using var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
    using var qrCode = new AsciiQRCode(qrData);
    var qrCodeAsAscii = qrCode.GetGraphic(1, "██", "  ", drawQuietZones: false);

    Console.WriteLine(qrCodeAsAscii);
    Console.WriteLine("\n[Управление готово к работе]");
}

// === ЭМУЛЯТОР ВВОДА ===

static void HandleClientCommand(string json, InputSimulator sim)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var action = root.GetProperty("action").GetString();

        if (action == "text")
        {
            var text = root.GetProperty("value").GetString();
            if (!string.IsNullOrEmpty(text))
            {
                sim.Keyboard.TextEntry(text);
                Console.WriteLine($"[Текст]: {text}");
            }
            return;
        }

        var keyName = root.GetProperty("key").GetString();
        if (Enum.TryParse<VirtualKeyCode>(keyName, true, out var vkCode))
        {
            if (action == "down") sim.Keyboard.KeyDown(vkCode);
            else if (action == "up") sim.Keyboard.KeyUp(vkCode);
        }
    }
    catch { }
}

// === ВЕБ-ИНТЕРФЕЙС ===

static string GetHtmlPage()
{
    return """
    <!DOCTYPE html>
    <html lang="ru">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
        <title>Remote Controller</title>
        <style>
            * { box-sizing: border-box; -webkit-touch-callout: none; -webkit-user-select: none; user-select: none; touch-action: manipulation; }
            body { margin: 0; padding: 16px; background-color: #121214; color: #e1e1e6; font-family: -apple-system, sans-serif; display: flex; flex-direction: column; align-items: center; min-height: 100vh; }
            #status-bar { margin-bottom: 16px; font-size: 13px; display: flex; align-items: center; gap: 8px; }
            .indicator { width: 9px; height: 9px; border-radius: 50%; background: #e53e3e; }
            .indicator.connected { background: #38a169; box-shadow: 0 0 8px #38a169; }
            .container { width: 100%; max-width: 340px; display: flex; flex-direction: column; gap: 12px; }
            .text-box-container { display: flex; gap: 8px; width: 100%; }
            #text-input { flex: 1; padding: 12px 14px; background: #202024; border: 2px solid #323238; border-radius: 10px; color: #fff; font-size: 15px; outline: none; -webkit-user-select: text; user-select: text; }
            #text-input:focus { border-color: #00b37e; }
            .send-btn { padding: 0 16px; background: #00875f; color: #fff; border: none; border-radius: 10px; font-size: 14px; font-weight: 600; }
            .grid { display: flex; flex-direction: column; gap: 8px; width: 100%; }
            .row { display: flex; gap: 8px; justify-content: center; width: 100%; }
            button.key { flex: 1; height: 52px; background: #202024; color: #e1e1e6; border: 2px solid #323238; border-radius: 10px; font-size: 14px; font-weight: 600; display: flex; justify-content: center; align-items: center; box-shadow: 0 3px #121214; }
            button.key:active, button.key.active { background: #00875f; border-color: #00b37e; transform: translateY(2px); box-shadow: 0 1px #121214; }
            .empty { flex: 1; height: 52px; visibility: hidden; }
            .system-key { background: #29292e; }
        </style>
    </head>
    <body>
        <div id="status-bar">
            <div id="dot" class="indicator"></div>
            <span id="status-text">Подключение...</span>
        </div>
        <div class="container">
            <form id="text-form" class="text-box-container" onsubmit="event.preventDefault(); sendText();">
                <input type="text" id="text-input" placeholder="Наберите текст..." autocomplete="off">
                <button type="submit" class="send-btn">Ввод</button>
            </form>
            <div class="grid">
                <div class="row">
                    <button class="key" data-key="INSERT">Insert</button>
                    <button class="key" data-key="HOME">Home</button>
                </div>
                <div class="row">
                    <button class="key system-key" data-key="MENU">Alt</button>
                    <button class="key system-key" data-key="TAB">Tab</button>
                    <button class="key system-key" data-key="ESCAPE">Esc</button>
                </div>
                <div class="row">
                    <button class="key system-key" data-key="SHIFT">Shift</button>
                    <button class="key system-key" data-key="BACK">⌫ Back</button>
                    <button class="key system-key" data-key="RETURN">⏎ Enter</button>
                </div>
                <div class="row" style="margin-top: 8px;">
                    <div class="empty"></div>
                    <button class="key" data-key="UP">▲</button>
                    <div class="empty"></div>
                </div>
                <div class="row">
                    <button class="key" data-key="LEFT">◀</button>
                    <button class="key" data-key="DOWN">▼</button>
                    <button class="key" data-key="RIGHT">▶</button>
                </div>
            </div>
        </div>
        <script>
            const dot = document.getElementById('dot');
            const statusText = document.getElementById('status-text');
            const textInput = document.getElementById('text-input');
            const wsUrl = `${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/ws`;
            let ws;

            function connect() {
                ws = new WebSocket(wsUrl);
                ws.onopen = () => { dot.classList.add('connected'); statusText.innerText = 'Подключено к ПК'; };
                ws.onclose = () => { dot.classList.remove('connected'); statusText.innerText = 'Связь потеряна. Переподключение...'; setTimeout(connect, 1500); };
                ws.onerror = () => ws.close();
            }
            connect();

            function sendCommand(action, key) {
                if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ action, key }));
            }

            function sendText() {
                const val = textInput.value;
                if (!val) return;
                if (ws && ws.readyState === WebSocket.OPEN) {
                    ws.send(JSON.stringify({ action: "text", value: val }));
                    textInput.value = '';
                }
            }

            document.querySelectorAll('.key').forEach(button => {
                const key = button.getAttribute('data-key');
                const down = (e) => { e.preventDefault(); button.classList.add('active'); sendCommand('down', key); };
                const up = (e) => { e.preventDefault(); button.classList.remove('active'); sendCommand('up', key); };
                button.addEventListener('touchstart', down, { passive: false });
                button.addEventListener('touchend', up, { passive: false });
                button.addEventListener('touchcancel', up, { passive: false });
                button.addEventListener('mousedown', down);
                button.addEventListener('mouseup', up);
                button.addEventListener('mouseleave', up);
            });
        </script>
    </body>
    </html>
    """;
}