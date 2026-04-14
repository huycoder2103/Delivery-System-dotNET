using Microsoft.AspNetCore.Mvc;
using Delivery_System.Models;
using Microsoft.EntityFrameworkCore;

namespace Delivery_System.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        private decimal ParseSafe(string? val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            return decimal.TryParse(val, out decimal res) ? res : 0;
        }

        public async Task<IActionResult> Index()
        {
            var userId = HttpContext.Session.GetString("UserID");

            // Lấy Ca làm việc hiện tại của User này (Status = "ACTIVE")
            var currentShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
            
            ViewBag.CurrentShift = currentShift;

            // Lấy thống kê sơ bộ cho ca làm việc (Nếu có)
            if (currentShift != null)
            {
                // Lấy danh sách đơn hàng đã chuyển của nhân viên này để tính toán (TR + CT)
                var myDeliveredOrders = await _context.TblOrders
                    .AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.ShipStatus == "Đã giao")
                    .ToListAsync();
                
                ViewBag.DeliveredCount = myDeliveredOrders.Count;
                ViewBag.TotalRevenue = myDeliveredOrders.Sum(o => ParseSafe(o.Tr) + ParseSafe(o.Ct));
            }

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}
