using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NotificationService;

public sealed class StatusNotificationConsumer(
    ConnectionFactory factory,
    IHubContext<OrdersHub> hub,
    ILogger<StatusNotificationConsumer> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = factory.CreateConnection();
        var channel = connection.CreateModel();
        channel.QueueDeclare("orders.status", durable: true, exclusive: false, autoDelete: false);
        channel.BasicQos(0, 10, false);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (_, delivery) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<OrderStatusChangedMessage>(delivery.Body.Span)
                    ?? throw new InvalidOperationException("Received an empty status message.");
                hub.Clients.Group($"order:{message.OrderId}")
                    .SendAsync("OrderStatusChanged", message, stoppingToken)
                    .GetAwaiter().GetResult();
                channel.BasicAck(delivery.DeliveryTag, multiple: false);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to notify clients about order status");
                channel.BasicNack(delivery.DeliveryTag, multiple: false, requeue: true);
            }
        };
        channel.BasicConsume("orders.status", autoAck: false, consumer);

        stoppingToken.Register(() =>
        {
            channel.Dispose();
            connection.Dispose();
        });
        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private sealed record OrderStatusChangedMessage(Guid OrderId, string Status);
}
