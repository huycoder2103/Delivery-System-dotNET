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
                // Chỉ lấy thống kê phát sinh trong Ca làm việc này
                var stats = await _context.TblOrders
                    .AsNoTracking()
                    .Where(o => o.ShiftId == currentShift.ShiftId && o.ShipStatus == "Đã giao")
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

        // 2. BẮT ĐẦU CA LÀM VIỆC
        [HttpPost]
        public async Task<IActionResult> StartShift()
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var activeShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (activeShift == null)
            {
                var newShift = new TblWorkShift
                {
                    StaffId = userId,
                    StartTime = TimeHelper.NowVni(),
                    Status = "ACTIVE"
                };
                _context.TblWorkShifts.Add(newShift);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Bắt đầu ca làm việc thành công!";
            }
            return RedirectToAction("Index");
        }

        // 3. KẾT THÚC CA LÀM VIỆC
        [HttpPost]
        public async Task<IActionResult> EndShift()
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var activeShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (activeShift != null)
            {
                activeShift.Status = "ENDED";
                activeShift.EndTime = TimeHelper.NowVni();
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã kết thúc ca làm việc!";
            }
            return RedirectToAction("Index");
        }
    }
}
