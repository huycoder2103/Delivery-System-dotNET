using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Microsoft.Extensions.Caching.Memory;

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

        // 1. Chuyến xe đi
        [HttpGet]
        public async Task<IActionResult> List(string departureFilter, string destinationFilter, string searchTruck, int page = 1)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            int pageSize = 20;

            // Cache StationList
            if (!_cache.TryGetValue("StationList", out List<TblStation> stations))
            {
                stations = await _context.TblStations.Where(s => s.IsActive == true).AsNoTracking().ToListAsync();
                _cache.Set("StationList", stations, TimeSpan.FromMinutes(30));
            }
            ViewBag.StationList = stations;
            
            var query = _context.VwTripLists.AsNoTracking().AsQueryable(); // Lấy tất cả chuyến xe

            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);
            if (!string.IsNullOrEmpty(searchTruck)) query = query.Where(t => t.LicensePlate.Contains(searchTruck));

            // Tính toán phân trang
            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            page = page < 1 ? 1 : (page > totalPages && totalPages > 0 ? totalPages : page);

            var list = await query.OrderByDescending(t => t.CreatedAt)
                                 .Skip((page - 1) * pageSize)
                                 .Take(pageSize)
                                 .ToListAsync();
            
            // Tính số lượng đơn hàng cho mỗi chuyến xe bằng 1 câu truy vấn duy nhất
            var tripIds = list.Select(t => t.TripId).ToList();
            var orderCounts = await _context.TblOrders
                .AsNoTracking()
                .Where(o => tripIds.Contains(o.TripId))
                .GroupBy(o => o.TripId)
                .Select(g => new { TripId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TripId, x => x.Count);
            
            ViewBag.OrderCounts = orderCounts;
            ViewBag.SearchTruck = searchTruck;
            ViewBag.IsArrivalPage = false;
            ViewBag.TotalPages = totalPages;
            ViewBag.CurrentPage = page;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_TripTableBody", list);
            }

            return View(list);
        }

        // 2. Chuyến xe đến
        [HttpGet]
        public async Task<IActionResult> ArrivalList(string departureFilter, string destinationFilter, string searchTruck, int page = 1)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            int pageSize = 20;

            if (!_cache.TryGetValue("StationList", out List<TblStation> stations))
            {
                stations = await _context.TblStations.Where(s => s.IsActive == true).AsNoTracking().ToListAsync();
                _cache.Set("StationList", stations, TimeSpan.FromMinutes(30));
            }
            ViewBag.StationList = stations;
            
            var query = _context.VwTripLists.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);
            if (!string.IsNullOrEmpty(searchTruck)) query = query.Where(t => t.LicensePlate.Contains(searchTruck));

            // Tính toán phân trang
            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            page = page < 1 ? 1 : (page > totalPages && totalPages > 0 ? totalPages : page);

            var list = await query.OrderByDescending(t => t.CreatedAt)
                                 .Skip((page - 1) * pageSize)
                                 .Take(pageSize)
                                 .ToListAsync();

            // Tính số lượng đơn hàng cho mỗi chuyến xe bằng 1 câu truy vấn duy nhất
            var tripIds = list.Select(t => t.TripId).ToList();
            var orderCounts = await _context.TblOrders
                .AsNoTracking()
                .Where(o => tripIds.Contains(o.TripId))
                .GroupBy(o => o.TripId)
                .Select(g => new { TripId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TripId, x => x.Count);
            
            ViewBag.OrderCounts = orderCounts;
            ViewBag.SearchTruck = searchTruck;
            ViewBag.IsArrivalPage = true;
            ViewBag.TotalPages = totalPages;
            ViewBag.CurrentPage = page;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_TripTableBody", list);
            }

            return View(list);
        }

        // 3. Trang tạo chuyến xe mới
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            if (!_cache.TryGetValue("StationList", out List<TblStation> stations))
            {
                stations = await _context.TblStations.Where(s => s.IsActive == true).AsNoTracking().ToListAsync();
                _cache.Set("StationList", stations, TimeSpan.FromMinutes(30));
            }
            ViewBag.StationList = stations;

            if (!_cache.TryGetValue("TruckList", out List<TblTruck> trucks))
            {
                trucks = await _context.TblTrucks.AsNoTracking().ToListAsync();
                _cache.Set("TruckList", trucks, TimeSpan.FromMinutes(30));
            }
            ViewBag.TruckList = trucks;
            
            return View();
        }

        // 4. Xử lý lưu chuyến xe
        [HttpPost]
        public async Task<IActionResult> Create(TblTrip trip)
        {
            trip.TripId = "TRP-" + DateTime.Now.ToString("HHmm");
            trip.StaffCreated = HttpContext.Session.GetString("UserID");
            trip.CreatedAt = DateTime.Now;
            trip.Status = "Đang đi";
            trip.TripType = "depart";
            
            _context.TblTrips.Add(trip);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Thêm chuyến xe mới thành công!";
            return RedirectToAction("List");
        }

        // 5. Trang gán hàng vào xe
        [HttpGet]
        public async Task<IActionResult> AssignGoods(string id)
        {
            var trip = await _context.VwTripLists.FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();

            var pendingOrders = await _context.TblOrders
                .Where(o => (o.TripId == null || o.TripId == "") && o.SendStation == trip.Departure && (o.IsDeleted == false || o.IsDeleted == null))
                .AsNoTracking().ToListAsync();

            ViewBag.Trip = trip;
            return View(pendingOrders);
        }

        // 6. Trang xem hàng trên xe
        [HttpGet]
        public async Task<IActionResult> ViewGoods(string id)
        {
            var trip = await _context.VwTripLists.FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();

            var ordersOnTrip = await _context.TblOrders.Where(o => o.TripId == id).AsNoTracking().ToListAsync();

            ViewBag.Trip = trip;
            return View(ordersOnTrip);
        }

        // 7. Xử lý xe đã đến trạm
        [HttpPost]
        public async Task<IActionResult> Arrive(string id)
        {
            var trip = await _context.TblTrips.FindAsync(id);
            if (trip != null)
            {
                trip.Status = "Đã đến";
                var orders = await _context.TblOrders.Where(o => o.TripId == id).ToListAsync();
                foreach (var o in orders)
                {
                    o.ShipStatus = "Đã chuyển";
                }
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Chuyến xe {id} đã cập bến thành công!";
            }
            return RedirectToAction("ArrivalList");
        }
    }
}
