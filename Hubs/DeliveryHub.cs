using Microsoft.AspNetCore.SignalR;

namespace Delivery_System.Hubs
{
    public class DeliveryHub : Hub
    {
        // Hub này sẽ là trạm trung chuyển thông báo
        // Không cần code logic ở đây, SignalR sẽ tự xử lý kết nối
    }
}
