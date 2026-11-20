using Microsoft.AspNetCore.SignalR;

namespace NotificationService;

public sealed class OrdersHub : Hub
{
    public Task WatchOrder(Guid orderId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"order:{orderId}");
}
