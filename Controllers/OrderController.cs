using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.SignalR;
using Delivery_System.Models;
using Delivery_System.Helpers;
using Delivery_System.Hubs;

namespace Delivery_System.Controllers
{
    public class OrderController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly IHubContext<DeliveryHub> _hubContext;

        public OrderController(AppDbContext context, IMemoryCache cache, IHubContext<DeliveryHub> hubContext)
        {
            _context = context;
            _cache = cache;
            _hubContext = hubContext;
        }

        private async Task<List<TblStation>> GetCachedStationsAsync()
        {
            const string stationCacheKey = "StationList";
            if (!_cache.TryGetValue(stationCacheKey, out List<TblStation>? stations))
            {
                stations = await _context.TblStations.AsNoTracking().Where(s => s.IsActive == true).ToListAsync();
                var cacheOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(60));
                _cache.Set(stationCacheKey, stations, cacheOptions);
            }
            return stations ?? new List<TblStation>();
        }

        [HttpGet]
        public async Task<IActionResult> List(string? sendStationFilter, string? receiveStationFilter, string? searchPhone, string? dateFilter, string statusFilter = "all", int page = 1)
        {
            const int pageSize = 20;
            if (page < 1) page = 1;

            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.TblOrders.AsNoTracking();

            if (statusFilter == "pending") query = query.Where(o => string.IsNullOrEmpty(o.TripId));
            else if (statusFilter == "shipped") query = query.Where(o => !string.IsNullOrEmpty(o.TripId) && o.ShipStatus != "Đã giao");
            else if (statusFilter == "delivered") query = query.Where(o => o.ShipStatus == "Đã giao");

            if (!string.IsNullOrEmpty(sendStationFilter)) query = query.Where(o => o.SendStation == sendStationFilter);
            if (!string.IsNullOrEmpty(receiveStationFilter)) query = query.Where(o => o.ReceiveStation == receiveStationFilter);
            if (!string.IsNullOrEmpty(searchPhone)) query = query.Where(o => o.SenderPhone != null && o.OrderId != null && o.ReceiverPhone != null && (o.SenderPhone.Contains(searchPhone) || o.ReceiverPhone.Contains(searchPhone) || o.OrderId.Contains(searchPhone)));
            
            if (!string.IsNullOrEmpty(dateFilter) && DateTime.TryParse(dateFilter, out DateTime dt))
            {
                var nextDay = dt.AddDays(1);
                query = query.Where(o => o.CreatedAt >= dt && o.CreatedAt < nextDay);
            }

            int totalRecords = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
            
            // 2. TỐI ƯU: Sử dụng Projection để chỉ lấy các trường cần thiết hiển thị lên View
            var list = await query.OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(o => new TblOrder {
                    OrderId = o.OrderId,
                    SenderName = o.SenderName,
                    SenderPhone = o.SenderPhone,
                    ReceiverName = o.ReceiverName,
                    ReceiverPhone = o.ReceiverPhone,
                    SendStation = o.SendStation,
                    ReceiveStation = o.ReceiveStation,
                    ItemName = o.ItemName,
                    Amount = o.Amount,
                    Tr = o.Tr,
                    Ct = o.Ct,
                    Note = o.Note,
                    StaffInput = o.StaffInput,
                    ShipStatus = o.ShipStatus,
                    TripId = o.TripId,
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();
            
            // 3. TỐI ƯU: Đơn giản hóa GroupBy để MySQL xử lý tốt hơn
            var counts = await _context.TblOrders
                .AsNoTracking()
                .GroupBy(o => new { o.ShipStatus, HasTripId = !string.IsNullOrEmpty(o.TripId) })
                .Select(g => new { 
                    Status = g.Key.ShipStatus, 
                    HasTrip = g.Key.HasTripId, 
                    Count = g.Count() 
                })
                .ToListAsync();

            ViewBag.CountPending = counts.Where(c => !c.HasTrip && c.Status != "Đã giao").Sum(c => c.Count);
            ViewBag.CountShipped = counts.Where(c => c.HasTrip && c.Status != "Đã giao").Sum(c => c.Count);
            ViewBag.CountDelivered = counts.Where(c => c.Status == "Đã giao").Sum(c => c.Count);
            ViewBag.CountAll     = ViewBag.CountPending + ViewBag.CountShipped + ViewBag.CountDelivered;
            
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchPhone = searchPhone;
            ViewBag.CurrentStatus = statusFilter;
            ViewBag.SendStationFilter = sendStationFilter;
            ViewBag.ReceiveStationFilter = receiveStationFilter;
            ViewBag.DateFilter = dateFilter;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return PartialView("_OrderTableBody", list);
            return View(list);
        }

        [HttpGet]
        public async Task<IActionResult> Ship(string id)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            
            var order = await _context.TblOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();
            if (role != "AD" && order.StaffInput != userId) return RedirectToAction("List");
            
            var matchingTrips = await _context.VwTripLists.AsNoTracking().Where(t => t.Departure == order.SendStation && t.Destination == order.ReceiveStation && t.Status == "Đang đi").ToListAsync();
            ViewBag.OrderForShip = order;
            return View(matchingTrips);
        }

        [HttpPost]
        public async Task<IActionResult> AssignToTrip(List<string> orderIds, string? orderId, string tripId, string source)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            if (orderIds == null) orderIds = new List<string>();
            if (!string.IsNullOrEmpty(orderId)) orderIds.Add(orderId);

            if (!orderIds.Any()) 
            {
                TempData["ErrorMessage"] = "Vui lòng chọn ít nhất một đơn hàng!";
                return (source == "ship") ? RedirectToAction("List") : RedirectToAction("List", "Trip");
            }

            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == tripId);
            if (trip == null)
            {
                TempData["ErrorMessage"] = "Chuyến xe không tồn tại!";
                return (source == "ship") ? RedirectToAction("List") : RedirectToAction("List", "Trip");
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () => {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try {
                    var orders = await _context.TblOrders.Where(o => orderIds.Contains(o.OrderId)).ToListAsync();
                    int successCount = 0;
                    foreach (var order in orders)
                    {
                        if (order.SendStation != trip.Departure || order.ReceiveStation != trip.Destination)
                        {
                            continue; // Bỏ qua đơn hàng không khớp trạm
                        }

                        if (role == "AD" || order.StaffInput == userId) {
                            order.TripId = tripId;
                            order.ShipStatus = "Đang chuyển";
                            successCount++;
                        }
                    }
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    if (successCount < orders.Count)
                    {
                        TempData["SuccessMessage"] = $"Đã gán thành công {successCount} đơn hàng. Một số đơn bị bỏ qua do không khớp trạm gửi/nhận.";
                    }
                    else
                    {
                        TempData["SuccessMessage"] = $"Đã gán thành công {successCount} đơn hàng vào chuyến xe {tripId}";
                    }
                } catch (Exception ex) {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
                }
            });
            
            return (source == "ship") ? RedirectToAction("List") : RedirectToAction("AssignGoods", "Trip", new { id = tripId });
        }

        [HttpPost]
        public async Task<IActionResult> RemoveFromTrip(string orderId, string tripId)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            var order = await _context.TblOrders.FindAsync(orderId);
            if (order == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng!";
                return RedirectToAction("ViewGoods", "Trip", new { id = tripId });
            }

            // Kiểm tra quyền: Admin hoặc người tạo đơn
            if (role != "AD" && order.StaffInput != userId)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền gỡ đơn hàng này!";
                return RedirectToAction("ViewGoods", "Trip", new { id = tripId });
            }

            // Chỉ gỡ khi đơn hàng đang ở trạng thái "Đang chuyển"
            if (order.ShipStatus == "Đã chuyển")
            {
                TempData["ErrorMessage"] = "Đơn hàng đã giao, không thể gỡ khỏi xe!";
                return RedirectToAction("ViewGoods", "Trip", new { id = tripId });
            }

            order.TripId = null;
            order.ShipStatus = "Chưa Chuyển";
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã gỡ đơn hàng {orderId} khỏi chuyến xe.";
            return RedirectToAction("ViewGoods", "Trip", new { id = tripId });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            
            var order = await _context.TblOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null || (role != "AD" && order.StaffInput != userId)) return RedirectToAction("List");
            
            ViewBag.StationList = await GetCachedStationsAsync();
            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(TblOrder order)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.StationList = await GetCachedStationsAsync();
                return View(order);
            }
            
            var userId = User.GetUserId();
            var role = User.GetRole();
            
            var existing = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == order.OrderId);
            if (existing != null && (role == "AD" || existing.StaffInput == userId)) {
                existing.ItemName = order.ItemName; existing.SendStation = order.SendStation; existing.ReceiveStation = order.ReceiveStation;
                existing.SenderName = string.IsNullOrWhiteSpace(order.SenderName) ? "None" : order.SenderName;
                existing.SenderPhone = order.SenderPhone;
                existing.ReceiverName = string.IsNullOrWhiteSpace(order.ReceiverName) ? "None" : order.ReceiverName;
                existing.ReceiverPhone = order.ReceiverPhone; existing.Amount = order.Amount; existing.Tr = order.Tr; existing.Ct = order.Ct; existing.Note = order.Note;
                await _context.SaveChangesAsync();

                // Gửi thông báo Real-time
                await _hubContext.Clients.All.SendAsync("UpdateOrderList");
            }
            return RedirectToAction("List");
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null && (role == "AD" || order.StaffInput == userId)) {
                order.IsDeleted = true;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("List");
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            var activeShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            ViewBag.HasActiveShift = (role == "AD" || activeShift != null);
            ViewBag.StationList = await GetCachedStationsAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(TblOrder order)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            var vniTime = TimeHelper.NowVni();

            // 1. Tự động tạo mã đơn hàng trước khi kiểm tra hợp lệ
            try {
                var lastOrder = await _context.TblOrders.AsNoTracking().IgnoreQueryFilters()
                    .Where(o => o.OrderId.StartsWith("ORD-"))
                    .OrderByDescending(o => o.OrderId)
                    .FirstOrDefaultAsync();

                int nextIdNum = 1;
                if (lastOrder != null && int.TryParse(lastOrder.OrderId.Replace("ORD-", ""), out int lastId)) 
                    nextIdNum = lastId + 1;

                order.OrderId = "ORD-" + nextIdNum.ToString("D6");
            } catch {
                order.OrderId = "ORD-" + DateTime.Now.Ticks.ToString().Substring(10);
            }

            // Xóa lỗi validate của OrderId vì chúng ta đã tự tạo ở trên
            ModelState.Remove("OrderId");

            // Đảm bảo giá trị decimal không bị null
            order.Tr ??= 0;
            order.Ct ??= 0;
            if (string.IsNullOrWhiteSpace(order.SenderName)) order.SenderName = "";
            if (string.IsNullOrWhiteSpace(order.ReceiverName)) order.ReceiverName = "";

            // Lấy ShiftId đang hoạt động của nhân viên
            var activeShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (!ModelState.IsValid)
            {
                ViewBag.HasActiveShift = (role == "AD" || activeShift != null);
                ViewBag.StationList = await GetCachedStationsAsync();
                return View(order);
            }

            if (role != "AD" && activeShift == null)
            {
                TempData["ErrorMessage"] = "Bạn chưa bắt đầu ca làm việc!";
                ViewBag.HasActiveShift = false;
                ViewBag.StationList = await GetCachedStationsAsync();
                return View(order);
            }

            try {
                order.StaffInput = userId;
                order.ShipStatus = "Chưa Chuyển";
                order.IsDeleted = false;
                order.CreatedAt = vniTime;
                order.ShiftId = activeShift?.ShiftId;

                _context.TblOrders.Add(order);
                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("UpdateOrderList");
                TempData["SuccessMessage"] = "Thêm đơn hàng thành công!";
                return RedirectToAction("List");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Lỗi lưu Database: " + ex.Message);
                ViewBag.HasActiveShift = (role == "AD" || activeShift != null);
                ViewBag.StationList = await GetCachedStationsAsync();
                return View(order);
            }
        }
        [HttpGet]
        public async Task<IActionResult> Trash()
        {
            var list = await _context.TblOrders.AsNoTracking().IgnoreQueryFilters().Where(o => o.IsDeleted == true).ToListAsync();
            return View(list);
        }

        [HttpPost]
        public async Task<IActionResult> Restore(string id)
        {
            var order = await _context.TblOrders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null) { order.IsDeleted = false; await _context.SaveChangesAsync(); }
            return RedirectToAction("Trash");
        }

        [HttpPost]
        public async Task<IActionResult> HardDelete(string id)
        {
            var order = await _context.TblOrders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null) { _context.TblOrders.Remove(order); await _context.SaveChangesAsync(); }
            return RedirectToAction("Trash");
        }

        [HttpGet]
        public async Task<IActionResult> PrintReceipt(string id, string type = "receipt")
        {
            var order = await _context.TblOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();
            
            ViewBag.PrintTime = TimeHelper.NowVni().ToString("dd/MM/yyyy HH:mm");
            
            if (type == "delivery")
            {
                return View("PrintDeliverySheet", order);
            }
            
            return View(order);
        }
    }
}
