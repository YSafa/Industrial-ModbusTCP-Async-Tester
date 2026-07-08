using System.Text.Json.Serialization;
using ModbusTester.Web;
using ModbusTester.Web.Hubs;

const string DevClientOrigin = "DevClientOrigin";

var builder = WebApplication.CreateBuilder(args);

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

app.MapControllers();
app.MapHub<ModbusHub>("/hubs/modbus");

app.Run();
