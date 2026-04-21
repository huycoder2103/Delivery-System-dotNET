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
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return PartialView("_TripTableBody", list);
            return View(list);
        }

        [HttpPost] public async Task<IActionResult> Arrive(string id) { var userId = User.GetUserId(); var role = User.GetRole(); var trip = await _context.TblTrips.FindAsync(id); if (trip == null) return NotFound(); var userStationName = User.GetStationName(); if (role != "AD" && trip.Destination != userStationName) return RedirectToAction("ArrivalList"); if (trip.Status != "Đã đến") { trip.Status = "Đã đến"; var orders = await _context.TblOrders.IgnoreQueryFilters().Where(o => o.TripId == id).ToListAsync(); foreach (var o in orders) o.ShipStatus = "Đã đến"; await _context.SaveChangesAsync(); await _hubContext.Clients.All.SendAsync("UpdateOrderList"); } return RedirectToAction("ArrivalList"); }
    }
}
