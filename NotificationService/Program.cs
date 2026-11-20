using NotificationService;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);
var rabbitUri = builder.Configuration["RabbitMq:Uri"]
    ?? throw new InvalidOperationException("RabbitMq:Uri is required.");

builder.Services.AddSignalR();
builder.Services.AddSingleton(_ => new ConnectionFactory { Uri = new Uri(rabbitUri) });
builder.Services.AddHostedService<StatusNotificationConsumer>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<OrdersHub>("/hubs/orders");
app.Run();
