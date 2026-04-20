using Microsoft.AspNetCore.SignalR;

namespace Delivery_System.Hubs
{
    public class DeliveryHub : Hub
    {
        // Hàm để Client tự đăng ký vào nhóm dựa trên ID trạm
        public async Task JoinStationGroup(string stationId, string role)
        {
            if (!string.IsNullOrEmpty(stationId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "Station_" + stationId);
            }
            
            // Nếu là Admin, cho vào nhóm "Admin" để nhận tất cả
            if (role == "AD")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "AdminGroup");
            }
        }
    }
}
