using System.Text.Json;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderWorker;

public sealed class OrderProcessor(
    IConfiguration configuration,
    ILogger<OrderProcessor> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString("Orders")
            ?? throw new InvalidOperationException("ConnectionStrings:Orders is required.");
        var rabbitUri = configuration["RabbitMq:Uri"]
            ?? throw new InvalidOperationException("RabbitMq:Uri is required.");

        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        var connection = new ConnectionFactory { Uri = new Uri(rabbitUri) }.CreateConnection();
        var channel = connection.CreateModel();
        channel.QueueDeclare("orders.created", durable: true, exclusive: false, autoDelete: false);
        channel.QueueDeclare("orders.status", durable: true, exclusive: false, autoDelete: false);
        channel.BasicQos(0, 1, false);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (_, delivery) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<OrderCreatedMessage>(delivery.Body.Span)
                    ?? throw new InvalidOperationException("Received an empty order message.");
                ProcessOrder(dataSource, channel, message.OrderId, stoppingToken);
                channel.BasicAck(delivery.DeliveryTag, multiple: false);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to process order message");
                try
                {
                    if (SetStatus(dataSource, GetOrderId(delivery.Body.Span), "Failed", "Processing") ||
                        SetStatus(dataSource, GetOrderId(delivery.Body.Span), "Failed", "Pending"))
                    {
                        PublishStatus(channel, GetOrderId(delivery.Body.Span), "Failed");
                    }
                }
                catch (Exception failureException)
                {
                    logger.LogError(failureException, "Could not persist failed status");
                }

                channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: false);
            }
        };
        channel.BasicConsume("orders.created", autoAck: false, consumer);

        stoppingToken.Register(() =>
        {
            channel.Dispose();
            connection.Dispose();
            dataSource.Dispose();
        });
        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static void ProcessOrder(
        NpgsqlDataSource dataSource,
        IModel channel,
        Guid orderId,
        CancellationToken stoppingToken)
    {
        var changed = SetStatus(dataSource, orderId, "Processing", "Pending");
        if (!changed)
        {
            return;
        }

        PublishStatus(channel, orderId, "Processing");
        Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).GetAwaiter().GetResult();

        if (SetStatus(dataSource, orderId, "Completed", "Processing"))
        {
            PublishStatus(channel, orderId, "Completed");
        }
    }

    private static bool SetStatus(
        NpgsqlDataSource dataSource,
        Guid orderId,
        string status,
        string expectedStatus)
    {
        using var command = dataSource.CreateCommand(
            """
            UPDATE orders
            SET status = $3, updated_at = now()
            WHERE id = $1 AND status = $2
            """);
        command.Parameters.AddWithValue(orderId);
        command.Parameters.AddWithValue(expectedStatus);
        command.Parameters.AddWithValue(status);
        return command.ExecuteNonQuery() > 0;
    }

    private static Guid GetOrderId(ReadOnlySpan<byte> body) =>
        JsonSerializer.Deserialize<OrderCreatedMessage>(body)?.OrderId
        ?? throw new InvalidOperationException("Received an invalid order message.");

    private static void PublishStatus(IModel channel, Guid orderId, string status)
    {
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        var body = JsonSerializer.SerializeToUtf8Bytes(new OrderStatusChangedMessage(orderId, status));
        channel.BasicPublish("", "orders.status", properties, body);
    }

    private sealed record OrderCreatedMessage(Guid OrderId);
    private sealed record OrderStatusChangedMessage(Guid OrderId, string Status);
}
