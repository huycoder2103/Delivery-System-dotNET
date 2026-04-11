using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Delivery_System.Models;

namespace Delivery_System.Controllers
{
    public class OrderController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        public OrderController(AppDbContext context, IMemoryCache cache)
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

        [HttpGet]
        public async Task<IActionResult> List(string? sendStationFilter, string? receiveStationFilter, string? searchPhone, string? dateFilter, string statusFilter = "all", int page = 1)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            const int pageSize = 20;
            if (page < 1) page = 1;

            ViewBag.StationList = await GetCachedStationsAsync();
            var query = _context.TblOrders.AsNoTracking().Where(o => o.IsDeleted == false || o.IsDeleted == null);

            if (statusFilter == "pending") query = query.Where(o => string.IsNullOrEmpty(o.TripId));
            else if (statusFilter == "shipped") query = query.Where(o => !string.IsNullOrEmpty(o.TripId));

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
            
            var list = await query.OrderByDescending(o => o.OrderId)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(o => new TblOrder {
                    OrderId = o.OrderId, ItemName = o.ItemName, SendStation = o.SendStation, ReceiveStation = o.ReceiveStation,
                    SenderName = o.SenderName, SenderPhone = o.SenderPhone, ReceiverName = o.ReceiverName, ReceiverPhone = o.ReceiverPhone,
                    StaffInput = o.StaffInput, Tr = o.Tr, Ct = o.Ct, Note = o.Note, CreatedAt = o.CreatedAt, TripId = o.TripId
                }).ToListAsync();
            
            var countsQuery = _context.TblOrders.AsNoTracking().Where(o => o.IsDeleted == false || o.IsDeleted == null);
            ViewBag.CountAll = await countsQuery.CountAsync();
            ViewBag.CountPending = await countsQuery.CountAsync(o => string.IsNullOrEmpty(o.TripId));
            ViewBag.CountShipped = await countsQuery.CountAsync(o => !string.IsNullOrEmpty(o.TripId));
            
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
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");
            var order = await _context.TblOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();
            if (role != "AD" && order.StaffInput != userId) return RedirectToAction("List");
            var matchingTrips = await _context.VwTripLists.AsNoTracking().Where(t => t.Departure == order.SendStation && t.Status == "Đang đi").ToListAsync();
            ViewBag.OrderForShip = order;
            return View(matchingTrips);
        }

        [HttpPost]
        public async Task<IActionResult> AssignToTrip(string orderId, string tripId, string source)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == orderId);
            if (order != null && (role == "AD" || order.StaffInput == userId)) {
                order.TripId = tripId;
                order.ShipStatus = "Đang chuyển";
                await _context.SaveChangesAsync();
            }
            return (source == "ship") ? RedirectToAction("List") : RedirectToAction("List", "Trip");
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");
            var order = await _context.TblOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null || (role != "AD" && order.StaffInput != userId)) return RedirectToAction("List");
            ViewBag.StationList = await GetCachedStationsAsync();
            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(TblOrder order)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
            var existing = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == order.OrderId);
            if (existing != null && (role == "AD" || existing.StaffInput == userId)) {
                existing.ItemName = order.ItemName; existing.SendStation = order.SendStation; existing.ReceiveStation = order.ReceiveStation;
                existing.SenderName = order.SenderName; existing.SenderPhone = order.SenderPhone; existing.ReceiverName = order.ReceiverName;
                existing.ReceiverPhone = order.ReceiverPhone; existing.Amount = order.Amount; existing.Tr = order.Tr; existing.Ct = order.Ct; existing.Note = order.Note;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("List");
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
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
            ViewBag.StationList = await GetCachedStationsAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(TblOrder order)
        {
            var vniTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"));
            var lastOrder = await _context.TblOrders.AsNoTracking().Where(o => o.OrderId.StartsWith("ORD-")).OrderByDescending(o => o.OrderId).FirstOrDefaultAsync();
            int nextIdNum = 1;
            if (lastOrder != null && int.TryParse(lastOrder.OrderId.Replace("ORD-", ""), out int lastId)) nextIdNum = lastId + 1;
            order.OrderId = "ORD-" + nextIdNum.ToString("D6");
            order.StaffInput = HttpContext.Session.GetString("UserID");
            order.ShipStatus = "Chưa Chuyển";
            order.IsDeleted = false;
            order.CreatedAt = vniTime;
            _context.TblOrders.Add(order);
            await _context.SaveChangesAsync();
            return RedirectToAction("List");
        }

        [HttpGet]
        public async Task<IActionResult> Trash()
        {
            var list = await _context.TblOrders.AsNoTracking().Where(o => o.IsDeleted == true).ToListAsync();
            return View(list);
        }

        [HttpPost]
        public async Task<IActionResult> Restore(string id)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null) { order.IsDeleted = false; await _context.SaveChangesAsync(); }
            return RedirectToAction("Trash");
        }

        [HttpPost]
        public async Task<IActionResult> HardDelete(string id)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null) { _context.TblOrders.Remove(order); await _context.SaveChangesAsync(); }
            return RedirectToAction("Trash");
        }

        [HttpGet]
        public async Task<IActionResult> PrintReceipt(string id)
        {
            var order = await _context.TblOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();
            return View(order);
        }
    }
}