using System.Net;
using System.Net.Sockets;
using Galaxies.Protocol;
using Microsoft.Extensions.Options;

namespace Galaxies.Server;

/// <summary>
/// The server's UDP socket. Receives datagrams and hands them to <see cref="GameHost"/>, and sends whatever the host
/// returns. On shutdown it tells every client the server is going away before closing the socket.
/// </summary>
public sealed class UdpServer : BackgroundService
{
    private readonly Socket _socket;
    private readonly GameHost _host;
    private readonly ILogger<UdpServer> _logger;

    public UdpServer(GameHost host, IOptions<GameOptions> options, ILogger<UdpServer> logger)
    {
        _host = host;
        _logger = logger;

        // One dual-mode socket serves both IPv4 and IPv6 clients.
        _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
        if (OperatingSystem.IsWindows())
        {
            // Windows reports "port unreachable" from a vanished client as an error on the next receive, which would
            // otherwise break the receive loop. Turn that behaviour off.
            const int SioUdpConnReset = unchecked((int)0x9800000C);
            _socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
        }
        _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Value.Port));
        _logger.LogInformation("Listening on UDP port {Port}", options.Value.Port);
    }

    public void Send(IEnumerable<Outgoing> packets)
    {
        Span<byte> buffer = stackalloc byte[ProtocolConstants.MaxPacketSize];
        foreach (var (to, packet) in packets)
        {
            var length = PacketCodec.Encode(packet, buffer);
            try
            {
                _socket.SendTo(buffer[..length], SocketFlags.None, to);
            }
            catch (SocketException ex)
            {
                // UDP is fire-and-forget; a failed send to one client mustn't affect the others.
                _logger.LogDebug(ex, "Send to {Address} failed", to);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Bigger than the largest valid packet, so oversized datagrams arrive whole and are rejected by the decoder.
        var buffer = new byte[ProtocolConstants.MaxPacketSize * 2];
        EndPoint any = new IPEndPoint(IPAddress.IPv6Any, 0);

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;
            try
            {
                result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, any, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.MessageSize or SocketError.ConnectionReset)
            {
                continue; // An oversized datagram, or an ICMP error from a client that went away.
            }

            var from = (IPEndPoint)result.RemoteEndPoint;
            Send(_host.Receive(from, buffer.AsSpan(0, result.ReceivedBytes), Environment.TickCount64));
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // UDP has no delivery guarantee, so send the notice twice. Clients that miss both still time out.
        var notices = _host.DisconnectAll(DisconnectReason.ServerShutdown);
        Send(notices);
        Send(notices);
        if (notices.Count > 0) _logger.LogInformation("Told {Count} client(s) the server is shutting down", notices.Count);
    }

    public override void Dispose()
    {
        _socket.Dispose();
        base.Dispose();
    }
}
