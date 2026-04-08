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

        public async Task<IActionResult> Index()
        {
            // Kiểm tra đăng nhập (Nếu chưa đăng nhập thì đuổi ra trang Login)
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Lấy Ca làm việc hiện tại của User này (Status = "ACTIVE")
            var currentShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
            
            ViewBag.CurrentShift = currentShift;

            // Lấy thống kê sơ bộ cho ca làm việc (Nếu có)
            if (currentShift != null)
            {
                // Ví dụ: Đếm số đơn hàng đã giao trong ca này
                ViewBag.DeliveredCount = await _context.TblOrders
                    .CountAsync(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null));
                
                ViewBag.TotalRevenue = await _context.TblOrders
                    .Where(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null))
                    .SumAsync(o => o.Amount ?? 0);
            }

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}
