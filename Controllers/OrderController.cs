using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;

namespace Delivery_System.Controllers
{
    public class OrderController : Controller
    {
        private readonly AppDbContext _context;

        public OrderController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Danh sách đơn hàng
        [HttpGet]
        public async Task<IActionResult> List(string sendStationFilter, string receiveStationFilter, string searchPhone)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            ViewBag.StationList = await _context.TblStations.Where(s => s.IsActive == true).ToListAsync();
            var query = _context.TblOrders.Where(o => o.IsDeleted == false || o.IsDeleted == null).AsQueryable();

            if (!string.IsNullOrEmpty(sendStationFilter)) query = query.Where(o => o.SendStation == sendStationFilter);
            if (!string.IsNullOrEmpty(receiveStationFilter)) query = query.Where(o => o.ReceiveStation == receiveStationFilter);
            if (!string.IsNullOrEmpty(searchPhone)) query = query.Where(o => o.SenderPhone.Contains(searchPhone) || o.ReceiverPhone.Contains(searchPhone));

            var list = await query.OrderByDescending(o => o.OrderId).ToListAsync();
            ViewBag.SearchPhone = searchPhone;
            return View(list);
        }

        // 2. Trang chọn chuyến xe để chuyển hàng
        [HttpGet]
        public async Task<IActionResult> Ship(string id)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();

            var matchingTrips = await _context.VwTripLists
                .Where(t => t.Departure == order.SendStation && t.Status == "Đang đi")
                .ToListAsync();

            ViewBag.OrderForShip = order;
            return View(matchingTrips);
        }

        // 3. Xử lý gán đơn hàng vào chuyến xe
        [HttpPost]
        public async Task<IActionResult> AssignToTrip(string orderId, string tripId, string source)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == orderId);
            if (order != null)
            {
                order.TripId = tripId;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Gán đơn hàng {orderId} lên chuyến {tripId} thành công!";
            }

            if (source == "ship") return RedirectToAction("List");
            return RedirectToAction("List", "Trip");
        }

        // 4. Trang chỉnh sửa đơn hàng
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();

            // KIỂM TRA QUYỀN: Chỉ người tạo đơn hoặc Admin mới được sửa
            if (role != "AD" && order.StaffInput != userId)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền sửa đơn hàng do người khác tạo!";
                return RedirectToAction("List");
            }
            
            ViewBag.StationList = await _context.TblStations.Where(s => s.IsActive == true).ToListAsync();
            return View(order);
        }

        // 5. Xử lý lưu chỉnh sửa
        [HttpPost]
        public async Task<IActionResult> Edit(TblOrder order)
        {
            var existing = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == order.OrderId);
            if (existing == null) return NotFound();

            // Cập nhật các trường cho phép sửa
            existing.ItemName = order.ItemName;
            existing.SendStation = order.SendStation;
            existing.ReceiveStation = order.ReceiveStation;
            existing.SenderName = order.SenderName;
            existing.SenderPhone = order.SenderPhone;
            existing.ReceiverName = order.ReceiverName;
            existing.ReceiverPhone = order.ReceiverPhone;
            existing.Amount = order.Amount;
            existing.Tr = order.Tr;
            existing.Ct = order.Ct;
            existing.Note = order.Note;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Cập nhật đơn hàng thành công!";
            return RedirectToAction("List");
        }

        // 6. Xóa tạm thời (vào thùng rác)
        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null)
            {
                order.IsDeleted = true;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã đưa đơn hàng vào thùng rác!";
            }
            return RedirectToAction("List");
        }

        // 7. Tạo đơn hàng mới
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.StationList = await _context.TblStations.Where(s => s.IsActive == true).ToListAsync();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(TblOrder order)
        {
            order.OrderId = "ORD-" + DateTime.Now.ToString("HHmmss");
            order.StaffInput = HttpContext.Session.GetString("UserID");
            order.ReceiveDate = DateTime.Now.ToString("dd/MM/yyyy");
            order.ShipStatus = "Chưa Chuyển";
            order.IsDeleted = false;
            order.CreatedAt = DateTime.Now;
            
            _context.TblOrders.Add(order);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Thêm đơn hàng mới thành công!";
            return RedirectToAction("List");
        }

        // 8. Thùng rác
        [HttpGet]
        public async Task<IActionResult> Trash()
        {
            var list = await _context.TblOrders.Where(o => o.IsDeleted == true).ToListAsync();
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
                TempData["SuccessMessage"] = "Khôi phục đơn hàng thành công!";
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
                TempData["SuccessMessage"] = "Đã xóa vĩnh viễn đơn hàng!";
            }
            return RedirectToAction("Trash");
        }
    }
}
