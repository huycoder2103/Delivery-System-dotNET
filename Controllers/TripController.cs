using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using System.Collections.Generic;
using Delivery_System.Models;
using Delivery_System.Helpers;

namespace Delivery_System.Controllers
{
    public class TripController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Delivery_System.Hubs.DeliveryHub> _hubContext;

        public TripController(AppDbContext context, IMemoryCache cache, Microsoft.AspNetCore.SignalR.IHubContext<Delivery_System.Hubs.DeliveryHub> hubContext)
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
        public async Task<IActionResult> List(string? departureFilter, string? destinationFilter, string? searchTruck, int page = 1)
        {
            const int pageSize = 10; if (page < 1) page = 1;
            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.VwTripLists.AsNoTracking();

            var role = User.GetRole();
            int? myStationId = User.GetStationId();
            string? myStationName = null;

            if (role != "AD" && myStationId.HasValue)
            {
                var station = await _context.TblStations.AsNoTracking().FirstOrDefaultAsync(s => s.StationId == myStationId.Value);
                myStationName = station?.StationName;
                if (!string.IsNullOrEmpty(myStationName)) query = query.Where(t => t.Departure == myStationName);
            }

            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);
            if (!string.IsNullOrEmpty(searchTruck)) query = query.Where(t => (t.LicensePlate != null && t.LicensePlate.Contains(searchTruck)) || (t.TripId != null && t.TripId.Contains(searchTruck)));

            int totalRecords = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
            var list = await query.OrderByDescending(t => t.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            
            var countQuery = _context.TblTrips.AsNoTracking();
            if (!string.IsNullOrEmpty(myStationName)) countQuery = countQuery.Where(t => t.Departure == myStationName);

            var countsRaw = await countQuery.GroupBy(t => t.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
            ViewBag.CountDeparting = countsRaw.Where(c => c.Status == "Đang đi").Sum(c => c.Count);
            ViewBag.CountArrived = countsRaw.Where(c => c.Status == "Đã đến").Sum(c => c.Count);
            ViewBag.CountAll = countsRaw.Sum(c => c.Count);

            var tripIds = list.Select(t => t.TripId).ToList();
            ViewBag.OrderCounts = await _context.TblOrders.AsNoTracking().Where(o => tripIds.Contains(o.TripId ?? "")).GroupBy(o => o.TripId).Select(g => new { TripId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.TripId!, x => x.Count);
            ViewBag.CurrentPage = page; ViewBag.TotalPages = totalPages; ViewBag.DepartureFilter = departureFilter; ViewBag.DestinationFilter = destinationFilter;
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return PartialView("_TripTableBody", list);
            return View(list);
        }

        [HttpGet]
        public async Task<IActionResult> ArrivalList(string? departureFilter, string? destinationFilter, string? searchTruck, int page = 1)
        {
            if (string.IsNullOrEmpty(User.GetUserId())) return RedirectToAction("Login", "Account");
            const int pageSize = 10; if (page < 1) page = 1;
            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.VwTripLists.AsNoTracking();

            var role = User.GetRole();
            int? myStationId = User.GetStationId();
            string? myStationName = null;

            if (role != "AD" && myStationId.HasValue)
            {
                var station = await _context.TblStations.AsNoTracking().FirstOrDefaultAsync(s => s.StationId == myStationId.Value);
                myStationName = station?.StationName;
                if (!string.IsNullOrEmpty(myStationName)) query = query.Where(t => t.Destination == myStationName);
            }

            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);
            if (!string.IsNullOrEmpty(searchTruck)) query = query.Where(t => (t.LicensePlate != null && t.LicensePlate.Contains(searchTruck)) || (t.TripId != null && t.TripId.Contains(searchTruck)));

            int totalRecords = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
            var list = await query.OrderByDescending(t => t.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var countQuery = _context.TblTrips.AsNoTracking();
            if (!string.IsNullOrEmpty(myStationName)) countQuery = countQuery.Where(t => t.Destination == myStationName);

            var countsRaw = await countQuery.GroupBy(t => t.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
            ViewBag.CountDeparting = countsRaw.Where(c => c.Status == "Đang đi").Sum(c => c.Count);
            ViewBag.CountArrived = countsRaw.Where(c => c.Status == "Đã đến").Sum(c => c.Count);
            ViewBag.CountAll = countsRaw.Sum(c => c.Count);

            var tripIds = list.Select(t => t.TripId).ToList();
            ViewBag.OrderCounts = await _context.TblOrders.AsNoTracking().Where(o => tripIds.Contains(o.TripId ?? "")).GroupBy(o => o.TripId).Select(g => new { TripId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.TripId!, x => x.Count);
            ViewBag.CurrentPage = page; ViewBag.TotalPages = totalPages; ViewBag.DepartureFilter = departureFilter; ViewBag.DestinationFilter = destinationFilter;
            ViewBag.IsArrivalPage = true;
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return PartialView("_TripTableBody", list);
            return View(list);
        }

        [HttpPost] public async Task<IActionResult> Arrive(string id) { var userId = User.GetUserId(); var role = User.GetRole(); var trip = await _context.TblTrips.FirstOrDefaultAsync(t => t.TripId == id); if (trip == null) return NotFound(); var userStationName = User.GetStationName(); if (role != "AD" && trip.Destination != userStationName) return Json(new { success = false, message = "Bạn không có quyền tiếp nhận chuyến xe tại trạm này!" }); if (trip.Status != "Đã đến") { trip.Status = "Đã đến"; trip.Notes = "[ARRIVED] " + userId + " | " + TimeHelper.NowVni().ToString("dd/MM HH:mm"); var orders = await _context.TblOrders.IgnoreQueryFilters().Where(o => o.TripId == id).ToListAsync(); foreach (var o in orders) o.ShipStatus = "Đã đến"; await _context.SaveChangesAsync(); await _hubContext.Clients.All.SendAsync("UpdateOrderList"); return Json(new { success = true }); } return Json(new { success = false, message = "Chuyến xe đã được tiếp nhận trước đó." }); }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            ViewBag.UserStationName = User.GetStationName();
            
            var activeShift = await _context.TblWorkShifts.AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
            
            ViewBag.HasActiveShift = (role == "AD" || activeShift != null);
            ViewBag.StationList = await GetCachedStationsAsync();
            ViewBag.TruckList = await _context.TblTrucks.AsNoTracking().Where(t => t.Status == true).ToListAsync();
            
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(TblTrip trip)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            var userStationName = User.GetStationName();

            // Loại bỏ các trường tự động sinh hoặc không có trong form khỏi Validation
            ModelState.Remove("TripId");
            ModelState.Remove("Truck");
            ModelState.Remove("TripType");
            ModelState.Remove("StaffCreatedNavigation");

            if (!ModelState.IsValid)
            {
                var activeShift = await _context.TblWorkShifts.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
                ViewBag.HasActiveShift = (role == "AD" || activeShift != null);
                ViewBag.StationList = await GetCachedStationsAsync();
                ViewBag.TruckList = await _context.TblTrucks.AsNoTracking().Where(t => t.Status == true).ToListAsync();
                ViewBag.UserStationName = userStationName;
                return View(trip);
            }

            try 
            {
                var activeShiftNow = await _context.TblWorkShifts.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

                if (role != "AD" && activeShiftNow == null)
                {
                    TempData["ErrorMessage"] = "Bạn chưa bắt đầu ca làm việc!";
                    return RedirectToAction("Create");
                }

                trip.TripId = await GenerateTripIdAsync();
                trip.Status = "Đang đi";
                trip.TripType = "depart"; // Mặc định là chuyến đi
                trip.StaffCreated = userId;
                trip.CreatedAt = TimeHelper.NowVni();
                trip.ShiftId = activeShiftNow?.ShiftId;

                _context.TblTrips.Add(trip);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Khởi tạo chuyến xe " + trip.TripId + " thành công!";
                return RedirectToAction("List");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Lỗi hệ thống: " + ex.Message);
                ViewBag.StationList = await GetCachedStationsAsync();
                ViewBag.TruckList = await _context.TblTrucks.AsNoTracking().Where(t => t.Status == true).ToListAsync();
                ViewBag.UserStationName = userStationName;
                return View(trip);
            }
        }

        private async Task<string> GenerateTripIdAsync()
        {
            try
            {
                var lastTrip = await _context.TblTrips.AsNoTracking().IgnoreQueryFilters()
                    .Where(t => t.TripId.StartsWith("TRP-"))
                    .OrderByDescending(t => t.TripId)
                    .FirstOrDefaultAsync();

                int nextIdNum = 1;
                if (lastTrip != null && int.TryParse(lastTrip.TripId.Replace("TRP-", ""), out int lastId))
                    nextIdNum = lastId + 1;

                return "TRP-" + nextIdNum.ToString("D6");
            }
            catch
            {
                return "TRP-" + DateTime.Now.Ticks.ToString().Substring(10);
            }
        }

        [HttpGet]
        public async Task<IActionResult> ViewGoods(string id)
        {
            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();

            var orders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.TripId == id && (o.IsDeleted == false || o.IsDeleted == null))
                .OrderBy(o => o.ReceiveStation)
                .ToListAsync();

            ViewBag.Trip = trip;
            ViewBag.StationList = await GetCachedStationsAsync();
            return View(orders);
        }

        [HttpGet]
        public async Task<IActionResult> PrintGoodsList(string id)
        {
            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();

            var orders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.TripId == id && (o.IsDeleted == false || o.IsDeleted == null))
                .OrderBy(o => o.ReceiveStation)
                .ToListAsync();

            ViewBag.Trip = trip;
            return View(orders);
        }

        [HttpGet]
        public async Task<IActionResult> AssignGoods(string id)
        {
            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();

            // Tìm các đơn hàng "Chưa Chuyển" có trạm gửi/nhận khớp với lộ trình của xe
            var availableOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => (o.IsDeleted == false || o.IsDeleted == null) &&
                            o.ShipStatus == "Chưa Chuyển" &&
                            o.SendStation == trip.Departure &&
                            o.ReceiveStation == trip.Destination &&
                            string.IsNullOrEmpty(o.TripId))
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            ViewBag.Trip = trip;
            return View(availableOrders);
        }
    }
}
