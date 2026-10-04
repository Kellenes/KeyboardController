using System.Net.WebSockets;
using System.Text;
using KeyboardController.Services;

const int Port = 8181;
var builder = WebApplication.CreateBuilder(args);

// Слушаем локальную петлю на порту 8181 для приема пакетов от SSH-туннеля
builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");

var app = builder.Build();

// Включаем автоматическую отдачу index.html из папки wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

var inputHandler = new InputHandler();

// Обработка WebSocket
app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("[+] Телефон подключен к пульту.");

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
                inputHandler.ProcessCommand(json);
            }
        }
    }
    catch { }
    finally
    {
        Console.WriteLine("[-] Телефон отключен.");
    }
});

// Запускаем веб-сервер
await app.StartAsync();
Console.WriteLine($"[✓] Локальный сервер готов: http://localhost:{Port}");

// Запускаем фоновый туннель
var tunnelService = new TunnelService();
Console.WriteLine("[*] Подключаем туннель...");
var publicUrl = await tunnelService.StartAsync(Port);

if (!string.IsNullOrEmpty(publicUrl))
{
    Console.WriteLine($"\n==========================================");
    Console.WriteLine($" Ссылка для телефона: {publicUrl}");
    Console.WriteLine($"==========================================\n");
}
else
{
    Console.WriteLine("[!] Не удалось получить ссылку туннеля. Проверьте SSH-подключение.");
}

await app.WaitForShutdownAsync();