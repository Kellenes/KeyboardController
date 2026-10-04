using System.Net.WebSockets;
using System.Text;
using KeyboardController.Services;

const int Port = 8181;
var builder = WebApplication.CreateBuilder(args);

// Listen on loclhost on port 8181
builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");

var app = builder.Build();

// Enable automatic serving of index.html from the wwwroot folder
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

var inputHandler = new InputHandler();

// Handle WebSocket
app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("[+] Device connected.");

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
        Console.WriteLine("[-] Device disconnected.");
    }
});

// Start the web server
await app.StartAsync();
Console.WriteLine($"[✓] Local server is ready: http://localhost:{Port}");

// Start the background tunnel
var tunnelService = new TunnelService();
Console.WriteLine("[*] Connecting tunnel...");
var publicUrl = await tunnelService.StartAsync(Port);

if (!string.IsNullOrEmpty(publicUrl))
{
    Console.WriteLine($"\n==========================================");
    Console.WriteLine($" URL: {publicUrl}");
    Console.WriteLine($"==========================================\n");
}
else
{
    Console.WriteLine("[!] Failed to obtain tunnel URL. Check your SSH connection.");
}

// Tray icon initialization
var trayService = new TrayService();
trayService.Initialize(publicUrl ?? $"http://localhost:{Port}");
Console.WriteLine("[*] Icon added to system tray");

await app.WaitForShutdownAsync();