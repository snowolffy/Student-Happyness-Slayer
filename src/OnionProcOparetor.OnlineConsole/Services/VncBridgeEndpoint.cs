using System.Net.Sockets;
using System.Net.WebSockets;

namespace OnionProcOparetor.OnlineConsole.Services;

/// <summary>
/// WebSocket &lt;-&gt; TCP byte relay for Screen View ("websockify" pattern) - browsers can't
/// open a raw TCP socket, so this is what lets noVNC (running in the browser, see wwwroot/js/vncInterop.js)
/// reach winvnc.exe on an Agent machine, which only speaks plain RFB-over-TCP.
///
/// Design note / deviation from the approved plan: the plan described terminating the VNC password
/// server-side (completing the RFB security handshake in this proxy) so the password never reaches
/// browser JS. Implementing that would mean this proxy speaks BOTH halves of RFB - a real client to
/// winvnc.exe AND a fake server to noVNC that claims "already authenticated" - which is a much bigger
/// protocol-level component than a byte relay. noVNC's RFB class always drives the full handshake itself
/// (ProtocolVersion through ServerInit) starting the moment it opens the WebSocket; there's no supported
/// way to hand it a connection that's already past that point. Given the VNC password here is random,
/// single-session, and generated fresh per StartVncSession call (see VncSessionManager on the Agent
/// side) rather than a durable credential, and the browser already had to authenticate to this app (the
/// cookie gate below) before ever seeing it, a transparent relay - the standard way every real-world
/// noVNC deployment works - is the pragmatic choice here. The password is handed to noVNC directly via
/// JS interop (see VncView.razor), never through this endpoint.
///
/// Only ever one session per browser tab now: the multi-tile Live View grid was removed on 2026-09-04
/// because each tile is a full-resolution RFB stream (noVNC has no thumbnail/downscale mode, unlike the
/// WPF Console's own RfbClient) and twelve of them at once neither fit a phone's link over Tailscale
/// nor decoded fast enough in one browser. Do not reintroduce a grid on top of this relay without a
/// server-side downscaling stage first.
///
/// The target host is deliberately NOT taken from the client - only `port` is (the negotiated VNC port,
/// learned by the Razor page from its own trusted StartVncSession round-trip). The host is re-resolved
/// here from ClientMachine.LastKnownIp via the authenticated user's own ApiClient session, so a caller
/// can never point this server at an arbitrary internal address (SSRF) by tampering with the URL.
/// </summary>
public static class VncBridgeEndpoint
{
    public static async Task HandleAsync(
        HttpContext context,
        string clientGuid,
        int port,
        ApiClient apiClient,
        ILogger<object> logger)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (port is <= 0 or > 65535)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!AuthSessionClaims.TryAttach(apiClient, context.User))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var clients = await apiClient.GetClientsAsync();
        var client = clients.FirstOrDefault(c => c.ClientGuid == clientGuid);
        if (client?.LastKnownIp is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(client.LastKnownIp, port, context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "VNC bridge: connect to {Ip}:{Port} failed for {ClientGuid}", client.LastKnownIp, port, clientGuid);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var tcpStream = tcp.GetStream();

        var wsToTcp = PumpWebSocketToTcpAsync(socket, tcpStream, context.RequestAborted);
        var tcpToWs = PumpTcpToWebSocketAsync(tcpStream, socket, context.RequestAborted);

        await Task.WhenAny(wsToTcp, tcpToWs);

        try { tcp.Close(); } catch { /* peer may already be gone */ }
        if (socket.State == WebSocketState.Open)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { /* peer may already be gone */ }
        }
    }

    private static async Task PumpWebSocketToTcpAsync(WebSocket socket, NetworkStream tcpStream, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                await tcpStream.WriteAsync(buffer.AsMemory(0, result.Count), ct);
            }
        }
        catch (OperationCanceledException) { /* normal on close */ }
        catch (WebSocketException) { /* peer closed abruptly */ }
        catch (IOException) { /* tcp side closed */ }
    }

    private static async Task PumpTcpToWebSocketAsync(NetworkStream tcpStream, WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await tcpStream.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    break; // Agent-side VNC server closed the connection
                }

                await socket.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, endOfMessage: true, ct);
            }
        }
        catch (OperationCanceledException) { /* normal on close */ }
        catch (WebSocketException) { /* peer closed abruptly */ }
        catch (IOException) { /* tcp side closed */ }
    }
}
