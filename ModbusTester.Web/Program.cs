using System.Diagnostics;
using System.Text.Json.Serialization;
using ModbusTester.Web;
using ModbusTester.Web.Hubs;

const string DevClientOrigin = "DevClientOrigin";
const string AppUrl = "http://localhost:5080";

var builder = WebApplication.CreateBuilder(args);

// Published/packaged runs (no launchSettings) don't set ASPNETCORE_URLS, so pin the port the
// client is built to talk to. Dev (`dotnet run`) already sets this via launchSettings.json and
// is left alone.
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
{
    builder.WebHost.UseUrls(AppUrl);
}

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSignalR();

builder.Services.AddCors(options => options.AddPolicy(DevClientOrigin, policy =>
    policy.WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

builder.Services.AddSingleton<ModbusSessionManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ModbusSessionManager>());
builder.Services.AddHostedService<ModbusHubBridgeService>();

var app = builder.Build();

app.UseCors(DevClientOrigin);

// Serves the built React app (ModbusTester.Client's `npm run build` output, copied into
// wwwroot by the BuildAndCopyClient publish target) alongside the API — see ModbusTester.Web.csproj.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<ModbusHub>("/hubs/modbus");
app.MapFallbackToFile("index.html");

// Only the published exe (Production) auto-launches a browser tab; `dotnet run` during
// development is left alone since the client is usually served separately via `npm run dev`.
if (OperatingSystem.IsWindows() && !builder.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try { Process.Start(new ProcessStartInfo(AppUrl) { UseShellExecute = true }); }
        catch { /* best-effort: user can still open the URL manually */ }
    });
}

app.Run();
