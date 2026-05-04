using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using System.Collections.Generic;
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
                stations = await _context.TblStations.AsNoTracking()
                    .Where(s => s.IsActive == true)
                    .ToListAsync();
                
                // Giảm xuống 1 phút để dễ test trên máy local
                var cacheOptions = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(1));
                _cache.Set(stationCacheKey, stations, cacheOptions);
            }
            return stations ?? new List<TblStation>();
        }

        [HttpGet]
        public async Task<IActionResult> List(string? sendStationFilter, string? receiveStationFilter, string? searchPhone, string? searchStaff, string? dateFilter, string statusFilter = "all", int page = 1, bool isCashFlow = false, string? deliveredByStaff = null, bool onlyCod = false)
        {
            const int pageSize = 10;
            if (page < 1) page = 1;
            DateTime dt; 

            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.TblOrders.AsNoTracking().Where(o => o.IsDeleted == false || o.IsDeleted == null);
            string? myStationName = null;

            // 1. Lọc theo nhân viên GIAO HÀNG (Dùng cho báo cáo drill-down)
            if (!string.IsNullOrEmpty(deliveredByStaff))
            {
                query = query.Where(o => o.StaffReceive == deliveredByStaff && o.ShipStatus == "Đã giao");
                if (onlyCod) query = query.Where(o => o.Ct > 0);

                if (!string.IsNullOrEmpty(dateFilter) && DateTime.TryParse(dateFilter, out dt))
                {
                    string dateStr = dt.ToString("dd/MM/yyyy");
                    query = query.Where(o => o.ReceiveDate != null && o.ReceiveDate.Contains(dateStr));
                }
            }
            else
            {
                var role = User.GetRole();
                int? myStationId = User.GetStationId();

                if (role != "AD" && myStationId.HasValue)
                {
                    var station = await _context.TblStations.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.StationId == myStationId.Value);
                    
                    myStationName = station?.StationName;

                    if (!string.IsNullOrEmpty(myStationName))
                    {
                        query = query.Where(o => o.SendStation == myStationName || o.ReceiveStation == myStationName);
                    }
                }

                if (!string.IsNullOrEmpty(searchStaff)) query = query.Where(o => o.StaffInput == searchStaff);

                if (!string.IsNullOrEmpty(dateFilter) && DateTime.TryParse(dateFilter, out dt))
                {
                    var nextDay = dt.AddDays(1);
                    string dateStr = dt.ToString("dd/MM/yyyy");

                    if (statusFilter == "delivered")
                    {
                        query = query.Where(o => o.ShipStatus == "Đã giao" && o.ReceiveDate != null && o.ReceiveDate.Contains(dateStr));
                        if (onlyCod) query = query.Where(o => o.Ct > 0);
                    }
                    else if (isCashFlow)
                    {
                        query = query.Where(o => ((o.CreatedAt >= dt && o.CreatedAt < nextDay) && (o.Tr > 0)) || (o.ShipStatus == "Đã giao" && o.ReceiveDate != null && o.ReceiveDate.StartsWith(dateStr) && (o.Ct > 0)));
                    }
                    else
                    {
                        query = query.Where(o => o.CreatedAt >= dt && o.CreatedAt < nextDay);
                    }
                }

                if (!isCashFlow)
                {
                    if (statusFilter == "waiting") query = query.Where(o => string.IsNullOrEmpty(o.TripId) && o.ShipStatus != "Đã giao");
                    else if (statusFilter == "shipping") query = query.Where(o => o.ShipStatus == "Đang chuyển");
                    else if (statusFilter == "arrived") 
                    {
                        var arrivedTripIds = await _context.TblTrips.AsNoTracking().Where(t => t.Status == "Đã đến").Select(t => t.TripId).ToListAsync();
                        query = query.Where(o => (o.ShipStatus == "Đã đến" || arrivedTripIds.Contains(o.TripId ?? "")) && o.ShipStatus != "Đã giao");
                    }
                    else if (statusFilter == "delivered")
                    {
                        query = query.Where(o => o.ShipStatus == "Đã giao");
                        if (onlyCod) query = query.Where(o => o.Ct > 0);
                        
                        if (!string.IsNullOrEmpty(dateFilter) && DateTime.TryParse(dateFilter, out dt))
                        {
                            string dateStr = dt.ToString("dd/MM/yyyy");
                            query = query.Where(o => o.ReceiveDate != null && o.ReceiveDate.Contains(dateStr));
                        }
                    }
                }

                if (!string.IsNullOrEmpty(sendStationFilter)) query = query.Where(o => o.SendStation == sendStationFilter);
                if (!string.IsNullOrEmpty(receiveStationFilter)) query = query.Where(o => o.ReceiveStation == receiveStationFilter);
                if (!string.IsNullOrEmpty(searchPhone))
                {
                    query = query.Where(o => (o.SenderPhone != null && o.SenderPhone.Contains(searchPhone)) || 
                                             (o.ReceiverPhone != null && o.ReceiverPhone.Contains(searchPhone)) || 
                                             (o.OrderId != null && o.OrderId.Contains(searchPhone)));
                }
            }

            int totalRecords = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
            
            var list = await query.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            
            var badgeQuery = _context.TblOrders.AsNoTracking().Where(o => o.IsDeleted == false || o.IsDeleted == null);
            if (!string.IsNullOrEmpty(myStationName)) badgeQuery = badgeQuery.Where(o => o.SendStation == myStationName || o.ReceiveStation == myStationName);

            var countsRaw = await badgeQuery.GroupBy(o => new { o.ShipStatus, HasTrip = !string.IsNullOrEmpty(o.TripId) }).Select(g => new { g.Key.ShipStatus, g.Key.HasTrip, Count = g.Count() }).ToListAsync();

            ViewBag.CountWaiting = countsRaw.Where(c => !c.HasTrip && c.ShipStatus != "Đã giao").Sum(c => c.Count);
            ViewBag.CountShipping = countsRaw.Where(c => c.ShipStatus == "Đang chuyển").Sum(c => c.Count);
            ViewBag.CountArrived = countsRaw.Where(c => c.ShipStatus == "Đã đến").Sum(c => c.Count);
            ViewBag.CountDelivered = countsRaw.Where(c => c.ShipStatus == "Đã giao").Sum(c => c.Count);
            ViewBag.CountAll = countsRaw.Sum(c => c.Count);
            
            ViewBag.CurrentPage = page; ViewBag.TotalPages = totalPages; ViewBag.SearchPhone = searchPhone; ViewBag.SearchStaff = searchStaff; ViewBag.CurrentStatus = statusFilter; ViewBag.SendStationFilter = sendStationFilter; ViewBag.ReceiveStationFilter = receiveStationFilter; ViewBag.DateFilter = dateFilter; ViewBag.IsCashFlow = isCashFlow;
            ViewBag.OnlyCod = onlyCod;

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

            if (role != "AD")
            {
                var user = await _context.TblUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
                if (user != null)
                {
                    var station = await _context.TblStations.AsNoTracking().FirstOrDefaultAsync(s => s.StationId == user.StationId);
                    if (order.SendStation != station?.StationName)
                    {
                        TempData["ErrorMessage"] = "Bạn không có quyền điều phối đơn hàng của trạm khác!";
                        return RedirectToAction("List");
                    }
                }
            }
            var matchingTrips = await _context.VwTripLists.AsNoTracking().Where(t => t.Departure == order.SendStation && t.Destination == order.ReceiveStation && t.Status == "Đang đi").ToListAsync();
            ViewBag.AvailableTrips = matchingTrips;
            return View(order);
        }
        [HttpPost] public async Task<IActionResult> AssignToTrip(List<string> orderIds, string? orderId, string tripId, string source) { var userId = User.GetUserId(); var role = User.GetRole(); if (orderIds == null) orderIds = new List<string>(); if (!string.IsNullOrEmpty(orderId)) orderIds.Add(orderId); if (!orderIds.Any()) { TempData["ErrorMessage"] = "Vui lòng chọn ít nhất một đơn hàng!"; return (source == "ship") ? RedirectToAction("List") : RedirectToAction("List", "Trip"); } var userStationName = User.GetStationName(); var result = await _orderService.AssignOrdersToTripAsync(orderIds, tripId, userId, role, userStationName); if (result.SuccessCount > 0) TempData["SuccessMessage"] = result.Message; else TempData["ErrorMessage"] = result.Message; return (source == "ship") ? RedirectToAction("List") : RedirectToAction("AssignGoods", "Trip", new { id = tripId }); }
        [HttpGet] public async Task<IActionResult> Create() { var userId = User.GetUserId(); var role = User.GetRole(); ViewBag.UserStationName = User.GetStationName(); var activeShift = await _context.TblWorkShifts.AsNoTracking().FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE"); ViewBag.HasActiveShift = (role == "AD" || activeShift != null); ViewBag.StationList = await GetCachedStationsAsync(); return View(); }
        [HttpPost] public async Task<IActionResult> Create(TblOrder order) { var userId = User.GetUserId(); var role = User.GetRole(); var userStationName = User.GetStationName(); ModelState.Remove("OrderId"); if (!ModelState.IsValid) { var activeShift = await _context.TblWorkShifts.AsNoTracking().FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE"); ViewBag.HasActiveShift = (role == "AD" || activeShift != null); ViewBag.StationList = await GetCachedStationsAsync(); ViewBag.UserStationName = userStationName; return View(order); } var result = await _orderService.CreateOrderAsync(order, userId, role, userStationName); if (result.Success) { TempData["SuccessMessage"] = result.Message; return RedirectToAction("List"); } else { if (result.Message.Contains("ca làm việc")) { TempData["ErrorMessage"] = result.Message; ViewBag.HasActiveShift = false; } else { ModelState.AddModelError("", result.Message); } ViewBag.StationList = await GetCachedStationsAsync(); ViewBag.UserStationName = userStationName; return View(order); } }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var order = await _context.TblOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();

            var userId = User.GetUserId();
            var role = User.GetRole();

            if (role != "AD" && order.StaffInput != userId)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền chỉnh sửa đơn hàng này!";
                return RedirectToAction("List");
            }

            if (!string.IsNullOrEmpty(order.TripId))
            {
                TempData["ErrorMessage"] = "Đơn hàng đã lên xe, không thể chỉnh sửa!";
                return RedirectToAction("List");
            }

            ViewBag.StationList = await GetCachedStationsAsync();
            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(TblOrder order)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            if (!ModelState.IsValid)
            {
                ViewBag.StationList = await GetCachedStationsAsync();
                return View(order);
            }

            var result = await _orderService.UpdateOrderAsync(order, userId, role);
            if (result.Success)
            {
                TempData["SuccessMessage"] = result.Message;
                return RedirectToAction("List");
            }

            ModelState.AddModelError("", result.Message);
            ViewBag.StationList = await GetCachedStationsAsync();
            return View(order);
        }

        [HttpPost] public async Task<IActionResult> Deliver(string id) { var userId = User.GetUserId(); var role = User.GetRole(); var userStation = User.GetStationName(); var activeShift = await _context.TblWorkShifts.FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE"); if (role != "AD" && activeShift == null) return Json(new { success = false, message = "Bạn chưa bắt đầu ca làm việc!" }); var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id); if (order == null) return Json(new { success = false, message = "Không tìm thấy đơn hàng." }); if (role != "AD" && order.ReceiveStation != userStation) return Json(new { success = false, message = $"Bạn không có quyền giao đơn hàng này. Trạm đích: {order.ReceiveStation}" }); order.ShipStatus = "Đã giao"; order.StaffReceive = userId; order.ReceiveDate = TimeHelper.NowVni().ToString("dd/MM/yyyy HH:mm"); await _context.SaveChangesAsync(); return Json(new { success = true, message = $"Đơn hàng {id} thành công!", tripId = order.TripId }); }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null)
            {
                if (!string.IsNullOrEmpty(order.TripId))
                {
                    TempData["ErrorMessage"] = "Không thể xóa đơn hàng đã lên xe!";
                    return RedirectToAction("List");
                }
                order.IsDeleted = true;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã xóa đơn hàng {id} vào thùng rác.";
            }
            return RedirectToAction("List");
        }

        [HttpGet]
        public async Task<IActionResult> Trash()
        {
            var list = await _context.TblOrders.Where(o => o.IsDeleted == true).OrderByDescending(o => o.CreatedAt).ToListAsync();
            return View(list);
        }

        [HttpPost]
        public async Task<IActionResult> Restore(string id)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null)
            {
                order.IsDeleted = false;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã khôi phục đơn hàng {id}.";
            }
            return RedirectToAction("Trash");
        }

        [HttpPost]
        public async Task<IActionResult> HardDelete(string id)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null)
            {
                _context.TblOrders.Remove(order);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã xóa vĩnh viễn đơn hàng {id}.";
            }
            return RedirectToAction("Trash");
        }

        [HttpGet]
        public async Task<IActionResult> PrintReceipt(string id, string type)
        {
            var order = await _context.TblOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();

            if (!string.IsNullOrEmpty(order.TripId) && order.ShipStatus != "Đã giao")
            {
                return Content("Hàng đã lên xe, không thể in lại biên lai!");
            }

            ViewBag.PrintTime = TimeHelper.NowVni().ToString("dd/MM/yyyy HH:mm:ss");

            if (type == "delivery")
            {
                return View("PrintDeliverySheet", order);
            }
            
            if (type == "delivered")
            {
                return View("PrintDeliveredReceipt", order);
            }

            return View(order);
        }

        [HttpGet]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> Tracking(string id)
        {
            if (string.IsNullOrEmpty(id)) return NotFound();
            
            var order = await _context.TblOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();
            
            if (!string.IsNullOrEmpty(order.TripId))
            {
                ViewBag.TripInfo = await _context.TblTrips.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == order.TripId);
            }
            
            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> RemoveFromTrip(string orderId, string tripId)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == orderId);
            if (order != null)
            {
                order.TripId = null;
                order.ShipStatus = "Chưa Chuyển";
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã gỡ đơn hàng {orderId} khỏi chuyến xe.";
            }
            return RedirectToAction("ViewGoods", "Trip", new { id = tripId });
        }
    }
}
