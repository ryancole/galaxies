using Microsoft.Extensions.Options;

namespace Galaxies.Server;

/// <summary>Ticks the game at a fixed wall-clock rate and sends each tick's snapshots.</summary>
public sealed class TickLoop(GameHost host, UdpServer udp, IOptions<GameOptions> options, ILogger<TickLoop> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(1 / options.Value.TicksPerSecond);
        logger.LogInformation("Tick loop running at {Rate} Hz", options.Value.TicksPerSecond);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    udp.Send(host.Tick(Environment.TickCount64));
                }
                catch (Exception ex)
                {
                    // A bug in one tick shouldn't take the server down; keep ticking and make noise.
                    logger.LogError(ex, "Tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
