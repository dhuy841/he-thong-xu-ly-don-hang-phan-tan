using OrderWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<OrderProcessor>();

var host = builder.Build();
host.Run();
