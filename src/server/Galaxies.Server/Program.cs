using Galaxies.Server;

// The game server: a plain background host with a UDP socket. No HTTP. See docs/protocol.md.
// Settings are read from next to the executable, so the server behaves the same whatever folder it's started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

builder.Services.Configure<GameOptions>(builder.Configuration.GetSection("Game"));
builder.Services.AddSingleton<GameHost>();

// Registered before the tick loop so it starts first and stops last: on shutdown the tick loop stops, then the
// socket tells clients the server is going away.
builder.Services.AddSingleton<UdpServer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UdpServer>());
builder.Services.AddHostedService<TickLoop>();

builder.Build().Run();
