using System.Text.Json.Serialization;
using ModbusTester.Web;
using ModbusTester.Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSignalR();

builder.Services.AddSingleton<ModbusSessionManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ModbusSessionManager>());
builder.Services.AddHostedService<ModbusHubBridgeService>();

var app = builder.Build();

app.MapControllers();
app.MapHub<ModbusHub>("/hubs/modbus");

app.Run();
