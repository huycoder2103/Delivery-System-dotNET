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
        private readonly Delivery_System.Services.IOrderService _orderService;

        public OrderController(AppDbContext context, IMemoryCache cache, IHubContext<DeliveryHub> hubContext, Delivery_System.Services.IOrderService orderService)
        {
            _context = context;
            _cache = cache;
            _hubContext = hubContext;
            _orderService = orderService;
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
        public async Task<IActionResult> List(string? sendStationFilter, string? receiveStationFilter, string? searchPhone, string? searchStaff, string? dateFilter, string statusFilter = "all", int page = 1, bool isCashFlow = false)
        {
            const int pageSize = 10;
            if (page < 1) page = 1;

            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.TblOrders.AsNoTracking();

            // 1. LỌC THEO MÃ NHÂN VIÊN (NẾU CÓ)
            if (!string.IsNullOrEmpty(searchStaff))
            {
                query = query.Where(o => o.StaffInput == searchStaff);
            }

            // 2. LỌC THEO DÒNG TIỀN (CASH FLOW) HOẶC NGÀY TẠO THÔNG THƯỜNG
            if (!string.IsNullOrEmpty(dateFilter) && DateTime.TryParse(dateFilter, out DateTime dt))
            {
                var nextDay = dt.AddDays(1);
                string dateStr = dt.ToString("dd/MM/yyyy");

                if (isCashFlow)
                {
                    // Lấy đơn tạo mới (thu cước) HOẶC đơn đã giao (thu COD) trong ngày
                    query = query.Where(o => 
                        ((o.CreatedAt >= dt && o.CreatedAt < nextDay) && (o.Tr > 0)) || 
                        (o.ShipStatus == "Đã giao" && o.ReceiveDate != null && o.ReceiveDate.StartsWith(dateStr) && (o.Ct > 0))
                    );
                }
                else
                {
                    // Mặc định chỉ lọc theo ngày tạo
                    query = query.Where(o => o.CreatedAt >= dt && o.CreatedAt < nextDay);
                }
            }

            // 3. LOGIC LỌC THEO TRẠNG THÁI (Chỉ áp dụng nếu không phải lọc CashFlow)
            if (!isCashFlow)
            {
                if (statusFilter == "waiting") 
                    query = query.Where(o => string.IsNullOrEmpty(o.TripId) && o.ShipStatus != "Đã giao");
                else if (statusFilter == "shipping") 
                    query = query.Where(o => o.ShipStatus == "Đang chuyển");
                else if (statusFilter == "arrived") 
                {
                    var arrivedTripIds = await _context.TblTrips.AsNoTracking()
                        .Where(t => t.Status == "Đã đến").Select(t => t.TripId).ToListAsync();
                    query = query.Where(o => arrivedTripIds.Contains(o.TripId ?? "") && o.ShipStatus != "Đã giao");
                }
                else if (statusFilter == "delivered") 
                    query = query.Where(o => o.ShipStatus == "Đã giao");
            }
            else
            {
                query = query.Where(o => o.IsDeleted == false || o.IsDeleted == null);
            }

            if (!string.IsNullOrEmpty(sendStationFilter)) query = query.Where(o => o.SendStation == sendStationFilter);
            if (!string.IsNullOrEmpty(receiveStationFilter)) query = query.Where(o => o.ReceiveStation == receiveStationFilter);
            if (!string.IsNullOrEmpty(searchPhone)) query = query.Where(o => o.SenderPhone != null && o.OrderId != null && o.ReceiverPhone != null && (o.SenderPhone.Contains(searchPhone) || o.ReceiverPhone.Contains(searchPhone) || o.OrderId.Contains(searchPhone)));
            
            int totalRecords = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
            
            // Projection lấy các trường cần thiết
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
                    CreatedAt = o.CreatedAt,
                    ReceiveDate = o.ReceiveDate
                })
                .ToListAsync();
            
            // Tính toán số lượng cho Badge/Select Filter một cách chính xác
            var countsRaw = await _context.TblOrders.AsNoTracking()
                .GroupBy(o => new { o.ShipStatus, HasTrip = !string.IsNullOrEmpty(o.TripId) })
                .Select(g => new { g.Key.ShipStatus, g.Key.HasTrip, Count = g.Count() })
                .ToListAsync();

            ViewBag.CountWaiting = countsRaw.Where(c => !c.HasTrip && c.ShipStatus != "Đã giao").Sum(c => c.Count);
            ViewBag.CountShipping = countsRaw.Where(c => c.ShipStatus == "Đang chuyển").Sum(c => c.Count);
            ViewBag.CountArrived = countsRaw.Where(c => c.ShipStatus == "Đã đến").Sum(c => c.Count);
            ViewBag.CountDelivered = countsRaw.Where(c => c.ShipStatus == "Đã giao").Sum(c => c.Count);
            ViewBag.CountAll = countsRaw.Sum(c => c.Count);
            
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.SearchPhone = searchPhone;
            ViewBag.SearchStaff = searchStaff;
            ViewBag.CurrentStatus = statusFilter;
            ViewBag.SendStationFilter = sendStationFilter;
            ViewBag.ReceiveStationFilter = receiveStationFilter;
            ViewBag.DateFilter = dateFilter;
            ViewBag.IsCashFlow = isCashFlow;

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
            
            // Cho phép Admin hoặc nhân viên tại trạm gửi được điều phối đơn hàng
            if (role != "AD")
            {
                var user = await _context.TblUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
                var userStation = (await _context.TblStations.AsNoTracking().FirstOrDefaultAsync(s => s.StationId == user.StationId))?.StationName;
                if (order.SendStation != userStation)
                {
                    TempData["ErrorMessage"] = "Bạn không có quyền điều phối đơn hàng của trạm khác!";
                    return RedirectToAction("List");
                }
            }
            
            var matchingTrips = await _context.VwTripLists.AsNoTracking()
                .Where(t => t.Departure == order.SendStation && t.Destination == order.ReceiveStation && t.Status == "Đang đi")
                .ToListAsync();

            ViewBag.AvailableTrips = matchingTrips;
            return View(order);
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

            var userStationName = User.GetStationName();
            var result = await _orderService.AssignOrdersToTripAsync(orderIds, tripId, userId, role, userStationName);

            if (result.SuccessCount > 0) TempData["SuccessMessage"] = result.Message;
            else TempData["ErrorMessage"] = result.Message;
            
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
            
            // RÀNG BUỘC: Chỉ cho phép sửa khi chưa lên xe (Chờ xe)
            if (!string.IsNullOrEmpty(order.TripId))
            {
                TempData["ErrorMessage"] = "Đơn hàng đã được gán vào chuyến xe, không thể chỉnh sửa thông tin!";
                return RedirectToAction("List");
            }

            // Lấy trạm của nhân viên để khóa trạm gửi
            var user = await _context.TblUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            int? sId = user?.StationId;
            ViewBag.UserStationName = (await _context.TblStations.AsNoTracking().FirstOrDefaultAsync(s => s.StationId == sId))?.StationName;

            ViewBag.StationList = await GetCachedStationsAsync();
            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(TblOrder order)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            // Nếu là nhân viên, cưỡng bức trạm gửi về trạm của họ (đề phòng can thiệp trình duyệt)
            if (role != "AD")
            {
                var user = await _context.TblUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
                int? sId = user?.StationId;
                var userStation = (await _context.TblStations.AsNoTracking().FirstOrDefaultAsync(s => s.StationId == sId))?.StationName;
                if (!string.IsNullOrEmpty(userStation)) order.SendStation = userStation;
            }

            if (!ModelState.IsValid)
            {
                ViewBag.StationList = await GetCachedStationsAsync();
                return View(order);
            }
            
            var existing = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == order.OrderId);
            if (existing != null && (role == "AD" || existing.StaffInput == userId)) {
                // Bảo vệ: Nếu hàng đã lên xe thì không được lưu thay đổi
                if (!string.IsNullOrEmpty(existing.TripId)) {
                    TempData["ErrorMessage"] = "Đơn hàng đã lên xe, không thể thay đổi thông tin!";
                    return RedirectToAction("List");
                }

                existing.ItemName = order.ItemName; 
                existing.SendStation = order.SendStation; 
                existing.ReceiveStation = order.ReceiveStation;
                existing.SenderName = string.IsNullOrWhiteSpace(order.SenderName) ? "" : order.SenderName;
                existing.SenderPhone = order.SenderPhone;
                existing.ReceiverName = string.IsNullOrWhiteSpace(order.ReceiverName) ? "" : order.ReceiverName;
                existing.ReceiverPhone = order.ReceiverPhone; 
                existing.Tr = order.Tr ?? 0; 
                existing.Ct = order.Ct ?? 0; 
                existing.Amount = existing.Tr + existing.Ct; 
                existing.Note = order.Note;
                await _context.SaveChangesAsync();

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
                // RÀNG BUỘC: Không cho xóa đơn hàng đã lên xe
                if (!string.IsNullOrEmpty(order.TripId)) {
                    TempData["ErrorMessage"] = "Đơn hàng đã lên xe, không thể xóa!";
                    return RedirectToAction("List");
                }

                order.IsDeleted = true;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã xóa đơn hàng " + id;
            }
            return RedirectToAction("List");
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            // TỐI ƯU: Lấy trạm từ Cookie, không gọi DB
            ViewBag.UserStationName = User.GetStationName();

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
            var userStationName = User.GetStationName();

            // Xóa OrderId khỏi ModelState vì sẽ được sinh tự động trong Service
            ModelState.Remove("OrderId");

            if (!ModelState.IsValid)
            {
                var activeShift = await _context.TblWorkShifts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
                ViewBag.HasActiveShift = (role == "AD" || activeShift != null);
                ViewBag.StationList = await GetCachedStationsAsync();
                ViewBag.UserStationName = userStationName;
                return View(order);
            }

            var result = await _orderService.CreateOrderAsync(order, userId, role, userStationName);

            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction("List");
            }
            else
            {
                if (result.Message.Contains("ca làm việc"))
                {
                    TempData["ErrorMessage"] = result.Message;
                    ViewBag.HasActiveShift = false;
                }
                else
                {
                    ModelState.AddModelError("", result.Message);
                }
                ViewBag.StationList = await GetCachedStationsAsync();
                ViewBag.UserStationName = userStationName;
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deliver(string id)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            var userStation = User.GetStationName();

            // Kiểm tra ca làm việc
            var activeShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (role != "AD" && activeShift == null)
            {
                return Json(new { success = false, message = "Bạn chưa bắt đầu ca làm việc! Không thể thực hiện giao hàng." });
            }

            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng." });

            // RÀNG BUỘC: Chỉ Admin hoặc nhân viên tại trạm đích mới được giao hàng
            if (role != "AD" && order.ReceiveStation != userStation)
            {
                return Json(new { success = false, message = $"Bạn không có quyền giao đơn hàng này. Đơn hàng này thuộc trạm đích: {order.ReceiveStation}" });
            }

            order.ShipStatus = "Đã giao";
            order.StaffReceive = userId; // Ghi nhận nhân viên thực hiện giao hàng cho khách
            order.ReceiveDate = TimeHelper.NowVni().ToString("dd/MM/yyyy HH:mm");
            
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                message = $"Đơn hàng {id} đã được giao thành công!",
                tripId = order.TripId 
            });
        }
    }
}
