# Hệ thống xử lý đơn hàng phân tán

MVP gồm ba service độc lập: **Order API**, **Order Worker** và **Notification Service**. PostgreSQL lưu đơn hàng; RabbitMQ vận chuyển yêu cầu và sự kiện; SignalR đẩy cập nhật đến client đang theo dõi đơn.

## Chạy local

Cần Docker Desktop và Docker Compose:

```powershell
docker compose up --build
```

- Order API: `http://localhost:5001`
- SignalR hub: `http://localhost:5002/hubs/orders`
- RabbitMQ Management: `http://localhost:15672` (đăng nhập `orders` / `orders_dev`)

Tạo đơn:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5001/orders `
  -ContentType 'application/json' `
  -Body '{"customer":"Nguyen Van A","items":[{"productName":"Book","quantity":2}]}'
```

Tra cứu: `GET http://localhost:5001/orders`, `GET http://localhost:5001/orders/{id}`. Danh sách hỗ trợ `limit` (1-100) và `offset`.

Client SignalR kết nối tới `/hubs/orders`, gọi `WatchOrder(orderId)` với GUID từ kết quả tạo đơn, rồi lắng nghe event `OrderStatusChanged`. Payload có `orderId` và `status`.

Worker mô phỏng xử lý trong 2 giây và phát lần lượt `Processing`, `Completed`. Trạng thái `Failed` được dành sẵn để biểu diễn lỗi xử lý.

## Lưu ý MVP

Thông tin đăng nhập Compose là thông tin phát triển local, không dùng khi triển khai thật. API/Hub hiện chưa có xác thực hay phân quyền; trong môi trường thực cần giới hạn quyền theo khách hàng. Việc ghi đơn và publish message hiện là hai thao tác riêng, nên triển khai production nên bổ sung transactional outbox và retry/dead-letter policy.
