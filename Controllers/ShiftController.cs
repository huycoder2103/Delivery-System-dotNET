using Microsoft.AspNetCore.Mvc;
using Delivery_System.Models;
using Microsoft.EntityFrameworkCore;

namespace Delivery_System.Controllers
{
    public class ShiftController : Controller
    {
        private readonly AppDbContext _context;

        public ShiftController(AppDbContext context)
        {
            _context = context;
        }

        // 1. BẮT ĐẦU CA LÀM VIỆC
        public async Task<IActionResult> Start()
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            // Kiểm tra xem đã có ca nào đang mở chưa
            var activeShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (activeShift == null)
            {
                var newShift = new TblWorkShift
                {
                    StaffId = userId,
                    StartTime = DateTime.Now,
                    Status = "ACTIVE"
                };
                _context.TblWorkShifts.Add(newShift);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Bắt đầu ca làm việc thành công!";
            }

            return RedirectToAction("Index", "Home");
        }

        // 2. KẾT THÚC CA LÀM VIỆC
        public async Task<IActionResult> End()
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var activeShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (activeShift != null)
            {
                activeShift.Status = "ENDED";
                activeShift.EndTime = DateTime.Now;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã kết thúc ca làm việc!";
            }

            return RedirectToAction("Index", "Home");
        }
    }
}
