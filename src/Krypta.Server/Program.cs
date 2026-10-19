using System.Diagnostics;
using Krypta.Server.Connections;
using Krypta.Server.Messaging;
using Krypta.Server.RateLimiting;
using Krypta.Server.Rooms;

const string WebSocketSubprotocol = "krypta.v1";
long startedAt = Stopwatch.GetTimestamp();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<ProtocolMessageSender>();
builder.Services.AddSingleton<ICommandHandler, CommandHandler>();
builder.Services.AddSingleton<ActionRateLimiter>();
builder.Services.AddSingleton<JoinRateLimiter>();
builder.Services.AddSingleton<MessageDispatcher>();
builder.Services.AddSingleton<ConnectionHandler>();
builder.Services.AddHostedService<HeartbeatService>();

WebApplication app = builder.Build();

app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new HealthResponse(
    "ok",
    (long)Stopwatch.GetElapsedTime(startedAt).TotalSeconds)));

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync(
            "È richiesto un handshake WebSocket.",
            context.RequestAborted);
        return;
    }

    if (!context.WebSockets.WebSocketRequestedProtocols.Contains(
            WebSocketSubprotocol,
            StringComparer.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync(
            $"È richiesto il sotto-protocollo {WebSocketSubprotocol}.",
            context.RequestAborted);
        return;
    }

    using System.Net.WebSockets.WebSocket socket =
        await context.WebSockets.AcceptWebSocketAsync(
        WebSocketSubprotocol);
    ConnectionHandler handler = context.RequestServices
        .GetRequiredService<ConnectionHandler>();
    await handler.HandleAsync(
        new WebSocketClientConnection(
            socket,
            context.Connection.RemoteIpAddress?.ToString()),
        context.RequestAborted);
});

app.Run();

public sealed record HealthResponse(string Status, long UptimeSeconds);

public partial class Program;
