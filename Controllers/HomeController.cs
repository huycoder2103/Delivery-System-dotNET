using Microsoft.AspNetCore.Mvc;
using Delivery_System.Models;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Helpers;

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
            var userId = User.GetUserId();

            // Lấy Ca làm việc hiện tại của User này (Status = "ACTIVE")
            var currentShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
            
            ViewBag.CurrentShift = currentShift;

            // Lấy thống kê sơ bộ cho ca làm việc (Nếu có)
            if (currentShift != null)
            {
                // Lấy danh sách đơn hàng đã chuyển của nhân viên này để tính toán (TR + CT)
                // Tính toán trực tiếp tại database để tối ưu
                var stats = await _context.TblOrders
                    .AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.ShipStatus == "Đã giao")
                    .GroupBy(o => 1)
                    .Select(g => new { 
                        Count = g.Count(), 
                        Revenue = g.Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0)) 
                    })
                    .FirstOrDefaultAsync();
                
                ViewBag.DeliveredCount = stats?.Count ?? 0;
                ViewBag.TotalRevenue = stats?.Revenue ?? 0;
            }

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }
    }
}
