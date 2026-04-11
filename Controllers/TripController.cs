using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Delivery_System.Models;

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
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30));
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
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(30));
                _cache.Set(truckCacheKey, trucks, cacheOptions);
            }
            return trucks ?? new List<TblTruck>();
        }

        [HttpGet]
        public async Task<IActionResult> List(string? departureFilter, string? destinationFilter, string? searchTruck, int page = 1)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");
            const int pageSize = 20; if (page < 1) page = 1;

            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.VwTripLists.AsNoTracking();
            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);
            if (!string.IsNullOrEmpty(searchTruck)) query = query.Where(t => t.LicensePlate != null && t.LicensePlate.Contains(searchTruck));

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
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");
            const int pageSize = 20; if (page < 1) page = 1;

            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.VwTripLists.AsNoTracking();
            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);
            if (!string.IsNullOrEmpty(searchTruck)) query = query.Where(t => t.LicensePlate != null && t.LicensePlate.Contains(searchTruck));

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
            ViewBag.StationList = await GetCachedStationsAsync();
            ViewBag.TruckList = await GetCachedTrucksAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(TblTrip trip)
        {
            var lastTrip = await _context.TblTrips.AsNoTracking().Where(t => t.TripId.StartsWith("TRP-")).OrderByDescending(t => t.TripId).FirstOrDefaultAsync();
            int nextIdNum = 1;
            if (lastTrip != null && int.TryParse(lastTrip.TripId.Replace("TRP-", ""), out int lastId)) nextIdNum = lastId + 1;
            trip.TripId = "TRP-" + nextIdNum.ToString("D6");
            trip.StaffCreated = HttpContext.Session.GetString("UserID");
            trip.CreatedAt = DateTime.Now;
            trip.Status = "Đang đi";
            trip.TripType = "depart";
            _context.TblTrips.Add(trip);
            await _context.SaveChangesAsync();
            return RedirectToAction("List");
        }

        [HttpGet]
        public async Task<IActionResult> AssignGoods(string id)
        {
            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();
            var pendingOrders = await _context.TblOrders.AsNoTracking().Where(o => string.IsNullOrEmpty(o.TripId) && o.SendStation == trip.Departure && (o.IsDeleted == false || o.IsDeleted == null)).ToListAsync();
            ViewBag.Trip = trip;
            return View(pendingOrders);
        }

        [HttpGet]
        public async Task<IActionResult> ViewGoods(string id)
        {
            var trip = await _context.VwTripLists.AsNoTracking().FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();
            var ordersOnTrip = await _context.TblOrders.AsNoTracking().Where(o => o.TripId == id).ToListAsync();
            ViewBag.Trip = trip;
            return View(ordersOnTrip);
        }

        [HttpPost]
        public async Task<IActionResult> Arrive(string id)
        {
            var trip = await _context.TblTrips.FindAsync(id);
            if (trip != null) {
                trip.Status = "Đã đến";
                var orders = await _context.TblOrders.Where(o => o.TripId == id).ToListAsync();
                foreach (var o in orders) o.ShipStatus = "Đã chuyển";
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("ArrivalList");
        }
    }
}