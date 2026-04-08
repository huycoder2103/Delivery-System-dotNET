using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;

namespace Delivery_System.Controllers
{
    public class TripController : Controller
    {
        private readonly AppDbContext _context;

        public TripController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Chuyến xe đi
        [HttpGet]
        public async Task<IActionResult> List(string departureFilter, string destinationFilter, string searchTruck)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            ViewBag.StationList = await _context.TblStations.Where(s => s.IsActive == true).ToListAsync();
            var query = _context.VwTripLists.Where(t => t.TripType == "depart").AsQueryable();

            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);
            if (!string.IsNullOrEmpty(searchTruck)) query = query.Where(t => t.LicensePlate.Contains(searchTruck));

            var list = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
            ViewBag.SearchTruck = searchTruck;
            return View(list);
        }

        // 2. Chuyến xe đến
        [HttpGet]
        public async Task<IActionResult> ArrivalList(string departureFilter, string destinationFilter)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            ViewBag.StationList = await _context.TblStations.Where(s => s.IsActive == true).ToListAsync();
            var query = _context.VwTripLists.Where(t => t.TripType == "arrival").AsQueryable();

            if (!string.IsNullOrEmpty(departureFilter)) query = query.Where(t => t.Departure == departureFilter);
            if (!string.IsNullOrEmpty(destinationFilter)) query = query.Where(t => t.Destination == destinationFilter);

            var list = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
            return View(list);
        }

        // 3. Trang tạo chuyến xe mới
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.StationList = await _context.TblStations.Where(s => s.IsActive == true).ToListAsync();
            ViewBag.TruckList = await _context.TblTrucks.ToListAsync();
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
                .ToListAsync();

            ViewBag.Trip = trip;
            return View(pendingOrders);
        }

        // 6. Trang xem hàng trên xe
        [HttpGet]
        public async Task<IActionResult> ViewGoods(string id)
        {
            var trip = await _context.VwTripLists.FirstOrDefaultAsync(t => t.TripId == id);
            if (trip == null) return NotFound();

            var ordersOnTrip = await _context.TblOrders.Where(o => o.TripId == id).ToListAsync();

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
