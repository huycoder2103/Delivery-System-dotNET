using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Delivery_System.Models;
using Delivery_System.Hubs;
using Delivery_System.Helpers;

namespace Delivery_System.Services
{
    public class OrderService : IOrderService
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;

        public OrderService(AppDbContext context, IHubContext<DeliveryHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task<string> GenerateOrderNumberAsync()
        {
            try
            {
                var lastOrder = await _context.TblOrders.AsNoTracking().IgnoreQueryFilters()
                    .Where(o => o.OrderId.StartsWith("ORD-"))
                    .OrderByDescending(o => o.OrderId)
                    .FirstOrDefaultAsync();

                int nextIdNum = 1;
                if (lastOrder != null && int.TryParse(lastOrder.OrderId.Replace("ORD-", ""), out int lastId))
                    nextIdNum = lastId + 1;

                return "ORD-" + nextIdNum.ToString("D6");
            }
            catch
            {
                return "ORD-" + DateTime.Now.Ticks.ToString().Substring(10);
            }
        }

        public async Task<(bool Success, string Message, TblOrder? Order)> CreateOrderAsync(TblOrder order, string userId, string role, string? userStationName)
        {
            var vniTime = TimeHelper.NowVni();

            // Ràng buộc trạm gửi: Nếu là NV thì trạm gửi PHẢI là trạm của họ
            if (role != "AD" && !string.IsNullOrEmpty(userStationName))
            {
                order.SendStation = userStationName;
            }

            // Sinh mã đơn hàng
            order.OrderId = await GenerateOrderNumberAsync();

            // Xử lý dữ liệu thô
            order.Tr ??= 0;
            order.Ct ??= 0;
            if (string.IsNullOrWhiteSpace(order.SenderName)) order.SenderName = "";
            if (string.IsNullOrWhiteSpace(order.ReceiverName)) order.ReceiverName = "";

            // Kiểm tra ca làm việc
            var activeShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (role != "AD" && activeShift == null)
            {
                return (false, "Bạn chưa bắt đầu ca làm việc!", null);
            }

            // Tính toán tổng tiền
            order.Amount = (order.Tr ?? 0) + (order.Ct ?? 0);

            try
            {
                order.StaffInput = userId;
                order.ShipStatus = "Chưa Chuyển";
                order.IsDeleted = false;
                order.CreatedAt = vniTime;
                order.ShiftId = activeShift?.ShiftId;

                _context.TblOrders.Add(order);
                await _context.SaveChangesAsync();

                // SIGNALR: Tìm ID của trạm dựa trên tên để gửi thông báo chính xác 100%
                var groups = new List<string> { "AdminGroup" };
                var stationNames = new List<string>();
                if (!string.IsNullOrEmpty(order.SendStation)) stationNames.Add(order.SendStation);
                if (!string.IsNullOrEmpty(order.ReceiveStation)) stationNames.Add(order.ReceiveStation);

                if (stationNames.Any())
                {
                    var stationIds = await _context.TblStations
                        .AsNoTracking()
                        .Where(s => stationNames.Contains(s.StationName))
                        .Select(s => s.StationId)
                        .ToListAsync();
                    
                    foreach (var sid in stationIds)
                    {
                        groups.Add("Station_" + sid);
                    }
                }

                await _hubContext.Clients.Groups(groups).SendAsync("UpdateOrderList");

                return (true, "Thêm đơn hàng thành công!", order);
            }
            catch (Exception ex)
            {
                return (false, "Lỗi lưu Database: " + ex.Message, null);
            }
        }

        public async Task<(bool Success, string Message)> UpdateOrderAsync(TblOrder order, string userId, string role)
        {
            var existing = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == order.OrderId);
            if (existing == null) return (false, "Không tìm thấy đơn hàng!");

            // Kiểm tra quyền: Admin hoặc người tạo đơn
            if (role != "AD" && existing.StaffInput != userId)
                return (false, "Bạn không có quyền chỉnh sửa đơn hàng này!");

            // Kiểm tra trạng thái: Chỉ cho phép sửa nếu chưa lên xe
            if (!string.IsNullOrEmpty(existing.TripId))
                return (false, "Đơn hàng đã lên xe, không thể chỉnh sửa!");

            try
            {
                existing.ItemName = order.ItemName;
                existing.ReceiverName = order.ReceiverName;
                existing.ReceiverPhone = order.ReceiverPhone;
                existing.SenderName = order.SenderName;
                existing.SenderPhone = order.SenderPhone;
                existing.ReceiveStation = order.ReceiveStation;
                existing.Note = order.Note;
                existing.Tr = order.Tr ?? 0;
                existing.Ct = order.Ct ?? 0;
                existing.Amount = (existing.Tr ?? 0) + (existing.Ct ?? 0);
                
                // Trạm gửi chỉ Admin mới được đổi
                if (role == "AD")
                {
                    existing.SendStation = order.SendStation;
                }

                await _context.SaveChangesAsync();

                // SignalR: Thông báo cập nhật
                var groups = new List<string> { "AdminGroup" };
                var stationNames = new List<string>();
                if (!string.IsNullOrEmpty(existing.SendStation)) stationNames.Add(existing.SendStation);
                if (!string.IsNullOrEmpty(existing.ReceiveStation)) stationNames.Add(existing.ReceiveStation);

                if (stationNames.Any())
                {
                    var stationIds = await _context.TblStations
                        .AsNoTracking()
                        .Where(s => stationNames.Contains(s.StationName))
                        .Select(s => s.StationId)
                        .ToListAsync();
                    
                    foreach (var sid in stationIds)
                    {
                        groups.Add("Station_" + sid);
                    }
                }
                await _hubContext.Clients.Groups(groups).SendAsync("UpdateOrderList");

                return (true, "Cập nhật đơn hàng thành công!");
            }
            catch (Exception ex)
            {
                return (false, "Lỗi khi cập nhật: " + ex.Message);
            }
        }

        public async Task<(int SuccessCount, string Message)> AssignOrdersToTripAsync(List<string> orderIds, string tripId, string userId, string role, string? userStationName)
        {
            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == tripId);
            if (trip == null)
            {
                return (0, "Chuyến xe không tồn tại!");
            }

            int successCount = 0;
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () => {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var orders = await _context.TblOrders.Where(o => orderIds.Contains(o.OrderId)).ToListAsync();
                    foreach (var order in orders)
                    {
                        // Kiểm tra khớp trạm
                        if (order.SendStation != trip.Departure || order.ReceiveStation != trip.Destination)
                        {
                            continue;
                        }

                        // QUYỀN: Admin hoặc nhân viên thuộc trạm gửi
                        if (role == "AD" || order.SendStation == userStationName)
                        {
                            order.TripId = tripId;
                            order.ShipStatus = "Đang chuyển";
                            successCount++;
                        }
                    }
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw new Exception("Có lỗi xảy ra khi gán chuyến: " + ex.Message);
                }
            });

            string msg = (successCount < orderIds.Count)
                ? $"Đã gán thành công {successCount} đơn hàng. Một số đơn bị bỏ qua do không khớp trạm hoặc không có quyền."
                : $"Đã gán thành công {successCount} đơn hàng vào chuyến xe {tripId}";

            return (successCount, msg);
        }
    }
}
