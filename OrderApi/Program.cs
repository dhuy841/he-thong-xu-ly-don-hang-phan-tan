using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Orders")
    ?? throw new InvalidOperationException("ConnectionStrings:Orders is required.");
var rabbitUri = builder.Configuration["RabbitMq:Uri"]
    ?? throw new InvalidOperationException("RabbitMq:Uri is required.");

builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
builder.Services.AddSingleton(_ => new ConnectionFactory { Uri = new Uri(rabbitUri) });

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/orders", async (CreateOrderRequest request, NpgsqlDataSource db, ConnectionFactory factory) =>
{
    if (string.IsNullOrWhiteSpace(request.Customer) ||
        request.Items is not { Count: > 0 } ||
        request.Items.Any(item => string.IsNullOrWhiteSpace(item.ProductName) || item.Quantity <= 0))
    {
        return Results.BadRequest(new { error = "Customer and at least one valid product are required." });
    }

    var id = Guid.NewGuid();
    var createdAt = DateTimeOffset.UtcNow;
    var items = JsonSerializer.Serialize(request.Items);

    await using (var command = db.CreateCommand(
        """
        INSERT INTO orders (id, customer, items, status, created_at, updated_at)
        VALUES ($1, $2, $3::jsonb, 'Pending', $4, $4)
        """))
    {
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(request.Customer.Trim());
        command.Parameters.AddWithValue(items);
        command.Parameters.AddWithValue(createdAt);
        await command.ExecuteNonQueryAsync();
    }

    try
    {
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare("orders.created", durable: true, exclusive: false, autoDelete: false);
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        var body = JsonSerializer.SerializeToUtf8Bytes(new OrderCreatedMessage(id));
        channel.BasicPublish("", "orders.created", properties, body);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Could not enqueue order {OrderId}", id);
        return Results.Problem(
            title: "Order saved but could not be queued. Check service logs and retry.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Created($"/orders/{id}", new
    {
        id,
        customer = request.Customer.Trim(),
        items = request.Items,
        status = "Pending",
        createdAt
    });
});

app.MapGet("/orders", async (NpgsqlDataSource db, int? limit, int? offset) =>
{
    var pageSize = Math.Clamp(limit ?? 50, 1, 100);
    var pageOffset = Math.Max(offset ?? 0, 0);
    var orders = new List<OrderResponse>();
    await using var command = db.CreateCommand(
        """
        SELECT id, customer, items, status, created_at, updated_at
        FROM orders
        ORDER BY created_at DESC
        LIMIT $1 OFFSET $2
        """);
    command.Parameters.AddWithValue(pageSize);
    command.Parameters.AddWithValue(pageOffset);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        orders.Add(ReadOrder(reader));
    }

    return Results.Ok(orders);
});

app.MapGet("/orders/{id:guid}", async (Guid id, NpgsqlDataSource db) =>
{
    await using var command = db.CreateCommand(
        "SELECT id, customer, items, status, created_at, updated_at FROM orders WHERE id = $1");
    command.Parameters.AddWithValue(id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.NotFound(new { error = "Order not found." });
    }

    return Results.Ok(ReadOrder(reader));
});

app.Run();

static OrderResponse ReadOrder(NpgsqlDataReader reader) => new(
    reader.GetGuid(0),
    reader.GetString(1),
    JsonSerializer.Deserialize<List<OrderItem>>(reader.GetString(2)) ?? [],
    reader.GetString(3),
    reader.GetFieldValue<DateTimeOffset>(4),
    reader.GetFieldValue<DateTimeOffset>(5));

record CreateOrderRequest(string Customer, List<OrderItem> Items);
record OrderItem(string ProductName, int Quantity);
record OrderCreatedMessage(Guid OrderId);
record OrderResponse(
    Guid Id,
    string Customer,
    List<OrderItem> Items,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
