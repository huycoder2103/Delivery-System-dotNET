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
            var userId = HttpContext.Session.GetString("UserID");

            // Lấy Ca làm việc hiện tại của User này (Status = "ACTIVE")
            var currentShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
            
            ViewBag.CurrentShift = currentShift;

            // Lấy thống kê sơ bộ cho ca làm việc (Nếu có)
            if (currentShift != null)
            {
                // TỐI ƯU: Sử dụng AsNoTracking & Global Filter đã tự động lọc IsDeleted.
                var stats = await _context.TblOrders
                    .AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển")
                    .GroupBy(o => 1)
                    .Select(g => new { 
                        Count = g.Count(), 
                        Sum = g.Sum(o => o.Amount ?? 0) 
                    })
                    .FirstOrDefaultAsync();
                
                ViewBag.DeliveredCount = stats?.Count ?? 0;
                ViewBag.TotalRevenue = stats?.Sum ?? 0;
            }

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}
