using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Delivery_System.Models;
using Delivery_System.Helpers;

namespace Delivery_System.Controllers
{
    public class TripController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        public TripController(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
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

        private async Task<List<TblTruck>> GetCachedTrucksAsync()
        {
            const string truckCacheKey = "TruckList";
            if (!_cache.TryGetValue(truckCacheKey, out List<TblTruck>? trucks))
            {
                trucks = await _context.TblTrucks.AsNoTracking().ToListAsync();
                var cacheOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(60));
                _cache.Set(truckCacheKey, trucks, cacheOptions);
            }
            return trucks ?? new List<TblTruck>();
        }

        [HttpGet]
        public async Task<IActionResult> List(string? departureFilter, string? destinationFilter, string? searchTruck, int page = 1)
        {
            const int pageSize = 10; if (page < 1) page = 1;

            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.VwTripLists.AsNoTracking();
            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);
            if (!string.IsNullOrEmpty(searchTruck)) query = query.Where(t => (t.LicensePlate != null && t.LicensePlate.Contains(searchTruck)) || (t.TripId != null && t.TripId.Contains(searchTruck)));

            int totalRecords = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
            var list = await query.OrderByDescending(t => t.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            
            var tripIds = list.Select(t => t.TripId).ToList();
            var orderCounts = await _context.TblOrders.AsNoTracking().Where(o => tripIds.Contains(o.TripId ?? "")).GroupBy(o => o.TripId)
                .Select(g => new { TripId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.TripId!, x => x.Count);
            
            ViewBag.OrderCounts = orderCounts; ViewBag.SearchTruck = searchTruck; ViewBag.IsArrivalPage = false;
            ViewBag.CurrentPage = page; ViewBag.TotalPages = totalPages; ViewBag.DepartureFilter = departureFilter; ViewBag.DestinationFilter = destinationFilter;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return PartialView("_TripTableBody", list);
            return View(list);
        }

        [HttpGet]
        public async Task<IActionResult> ArrivalList(string? departureFilter, string? destinationFilter, string? searchTruck, int page = 1)
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");
            const int pageSize = 10; if (page < 1) page = 1;

            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.VwTripLists.AsNoTracking();
            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);
            if (!string.IsNullOrEmpty(searchTruck)) query = query.Where(t => (t.LicensePlate != null && t.LicensePlate.Contains(searchTruck)) || (t.TripId != null && t.TripId.Contains(searchTruck)));

            int totalRecords = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
            var list = await query.OrderByDescending(t => t.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var tripIds = list.Select(t => t.TripId).ToList();
            var orderCounts = await _context.TblOrders.AsNoTracking().Where(o => tripIds.Contains(o.TripId ?? "")).GroupBy(o => o.TripId)
                .Select(g => new { TripId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.TripId!, x => x.Count);
            
            ViewBag.OrderCounts = orderCounts; ViewBag.SearchTruck = searchTruck; ViewBag.IsArrivalPage = true;
            ViewBag.CurrentPage = page; ViewBag.TotalPages = totalPages; ViewBag.DepartureFilter = departureFilter; ViewBag.DestinationFilter = destinationFilter;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return PartialView("_TripTableBody", list);
            return View(list);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            // TỐI ƯU: Lấy trạm từ Cookie
            var userStationName = User.GetStationName();
            ViewBag.UserStationName = userStationName;

            var activeShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            ViewBag.HasActiveShift = (role == "AD" || activeShift != null);
            ViewBag.StationList = await GetCachedStationsAsync();
            ViewBag.TruckList = await GetCachedTrucksAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(TblTrip trip)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            // TỐI ƯU: Lấy trạm từ Cookie
            var userStationName = User.GetStationName();

            // Ràng buộc trạm đi: Nếu là NV thì trạm đi PHẢI là trạm của họ
            if (role != "AD")
            {
                if (!string.IsNullOrEmpty(userStationName)) trip.Departure = userStationName;
                else
                {
                    ModelState.AddModelError("", "Tài khoản của bạn chưa được gán trạm làm việc. Vui lòng liên hệ Admin.");
                }
            }

            // Lấy ShiftId đang hoạt động của nhân viên
            var activeShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            // Tự động tạo mã chuyến xe
            try {
                var lastTrip = await _context.TblTrips.AsNoTracking()
                    .Where(t => t.TripId.StartsWith("TRP-"))
                    .OrderByDescending(t => t.TripId)
                    .FirstOrDefaultAsync();
                int nextIdNum = 1;
                if (lastTrip != null && int.TryParse(lastTrip.TripId.Replace("TRP-", ""), out int lastId)) 
                    nextIdNum = lastId + 1;
                trip.TripId = "TRP-" + nextIdNum.ToString("D6");
            } catch {
                trip.TripId = "TRP-" + DateTime.Now.Ticks.ToString().Substring(10);
            }

            ModelState.Remove("TripId");
            ModelState.Remove("TripType");
            ModelState.Remove("Truck");

            if (role != "AD" && activeShift == null)
            {
                TempData["ErrorMessage"] = "Bạn chưa bắt đầu ca làm việc!";
                ViewBag.HasActiveShift = false;
                ViewBag.StationList = await GetCachedStationsAsync();
                ViewBag.TruckList = await GetCachedTrucksAsync();
                ViewBag.UserStationName = userStationName;
                return View(trip);
            }

            if (!ModelState.IsValid)
            {
                ViewBag.HasActiveShift = (role == "AD" || activeShift != null);
                ViewBag.StationList = await GetCachedStationsAsync();
                ViewBag.TruckList = await GetCachedTrucksAsync();
                ViewBag.UserStationName = userStationName;
                return View(trip);
            }

            try {
                trip.StaffCreated = userId;
                trip.CreatedAt = TimeHelper.NowVni();
                trip.Status = "Đang đi";
                trip.TripType = "depart";
                trip.ShiftId = activeShift?.ShiftId;

                _context.TblTrips.Add(trip);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Tạo chuyến xe mới thành công!";
                return RedirectToAction("List");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Lỗi lưu Database: " + ex.Message);
                ViewBag.HasActiveShift = (role == "AD" || activeShift != null);
                ViewBag.StationList = await GetCachedStationsAsync();
                ViewBag.TruckList = await GetCachedTrucksAsync();
                ViewBag.UserStationName = userStationName;
                return View(trip);
            }
        }

        [HttpGet]
        public async Task<IActionResult> AssignGoods(string id)
        {
            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();
            var pendingOrders = await _context.TblOrders.AsNoTracking().Where(o => string.IsNullOrEmpty(o.TripId) && o.SendStation == trip.Departure && o.ReceiveStation == trip.Destination && (o.IsDeleted == false || o.IsDeleted == null)).ToListAsync();
            ViewBag.Trip = trip;
            return View(pendingOrders);
        }

        [HttpGet]
        public async Task<IActionResult> ViewGoods(string id)
        {
            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();
            var ordersOnTrip = await _context.TblOrders.AsNoTracking().Where(o => o.TripId == id).ToListAsync();
            
            ViewBag.StationList = await _context.TblStations.AsNoTracking().ToListAsync();
            ViewBag.Trip = trip;
            return View(ordersOnTrip);
        }

        [HttpGet]
        public async Task<IActionResult> PrintGoodsList(string id)
        {
            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();
            var ordersOnTrip = await _context.TblOrders.AsNoTracking().Where(o => o.TripId == id).ToListAsync();

            ViewBag.StationList = await _context.TblStations.AsNoTracking().ToListAsync();
            ViewBag.Trip = trip;
            return View(ordersOnTrip);
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveTripsByStation(string stationName)
        {
            var trips = await _context.VwTripLists
                .AsNoTracking()
                .Where(t => t.Departure == stationName && t.Status == "Đang đi")
                .ToListAsync();
            return PartialView("_TripSelectOptions", trips);
        }

        [HttpPost]
        public async Task<IActionResult> Arrive(string id)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();
            
            var trip = await _context.TblTrips.FindAsync(id);
            if (trip == null) return NotFound();

            // TỐI ƯU: Lấy trạm từ Cookie
            if (role != "AD")
            {
                var userStationName = User.GetStationName();
                if (string.IsNullOrEmpty(userStationName) || trip.Destination != userStationName)
                {
                    TempData["ErrorMessage"] = "Bạn không có quyền xác nhận chuyến xe này. Chỉ nhân viên tại trạm đích (" + trip.Destination + ") mới được thực hiện.";
                    return RedirectToAction("ArrivalList");
                }
            }

            if (trip.Status != "Đã đến")
            {
                trip.Status = "Đã đến";
                
                // Ghi nhận MÃ NHÂN VIÊN (ID) và thời gian đến vào Notes
                trip.Notes = $"[ARRIVED] {userId} | {TimeHelper.NowVni():dd/MM HH:mm}";

                // Lấy tất cả đơn hàng trên chuyến xe này (bỏ qua filter để chắc chắn cập nhật hết)
                var orders = await _context.TblOrders
                    .IgnoreQueryFilters()
                    .Where(o => o.TripId == id)
                    .ToListAsync();

                foreach (var o in orders)
                {
                    o.ShipStatus = "Đã đến";
                    // Không điền StaffReceive ở đây nữa, chỉ điền khi thực sự giao cho khách
                }
                
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã xác nhận chuyến {id} cập bến. {orders.Count} đơn hàng đã chuyển trạng thái thành công!";
            }
            return RedirectToAction("ArrivalList");
        }
    }
}