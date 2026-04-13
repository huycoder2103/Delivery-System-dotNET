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

        private bool IsAdmin() => HttpContext.Session.GetString("Role") == "AD";

        // 1. TRANG QUẢN LÝ CA LÀM VIỆC (DÀNH CHO ADMIN)
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");

            var shifts = await _context.TblWorkShifts
                .Include(s => s.Staff)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();

            return View(shifts);
        }

        // 2. BẮT ĐẦU CA LÀM VIỆC
        [HttpPost]
        public async Task<IActionResult> Start()
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var activeShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (activeShift == null)
            {
                var newShift = new TblWorkShift
                {
                    StaffId = userId,
                    StartTime = Delivery_System.Helpers.TimeHelper.NowVni(),
                    Status = "ACTIVE"
                };
                _context.TblWorkShifts.Add(newShift);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Bắt đầu ca làm việc thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = "Bạn đang có một ca làm việc hoạt động!";
            }

            return RedirectToAction("Index", "Home");
        }

        // 3. KẾT THÚC CA LÀM VIỆC
        [HttpPost]
        public async Task<IActionResult> End()
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var activeShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (activeShift != null)
            {
                activeShift.Status = "ENDED";
                activeShift.EndTime = Delivery_System.Helpers.TimeHelper.NowVni();
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã kết thúc ca làm việc!";
            }
            else
            {
                TempData["ErrorMessage"] = "Không tìm thấy ca làm việc đang hoạt động!";
            }

            return RedirectToAction("Index", "Home");
        }

        // 4. ADMIN KẾT THÚC HỘ CA LÀM VIỆC
        [HttpPost]
        public async Task<IActionResult> ForceEnd(int shiftId)
        {
            if (!IsAdmin()) return Forbid();

            var shift = await _context.TblWorkShifts.FindAsync(shiftId);
            if (shift != null && shift.Status == "ACTIVE")
            {
                shift.Status = "ENDED";
                shift.EndTime = Delivery_System.Helpers.TimeHelper.NowVni();
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã kết thúc ca làm việc của nhân viên!";
            }
            return RedirectToAction("Index");
        }

        // 5. CHI TIẾT HOẠT ĐỘNG TRONG CA (ADMIN XEM)
        public async Task<IActionResult> Details(int id)
        {
            if (!IsAdmin()) return Forbid();

            var shift = await _context.TblWorkShifts
                .Include(s => s.Staff)
                .FirstOrDefaultAsync(s => s.ShiftId == id);

            if (shift == null) return NotFound();

            var addedOrders = await _context.TblOrders.AsNoTracking().Where(o => o.ShiftId == id).ToListAsync();
            
            ViewBag.AddedOrders = addedOrders;
            ViewBag.TotalRevenue = addedOrders.Sum(o => o.Amount ?? 0); // Tính tổng doanh thu

            ViewBag.ReceivedOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.StaffReceive == shift.StaffId && o.ShipStatus == "Đã chuyển" 
                            && o.CreatedAt >= shift.StartTime && (shift.EndTime == null || o.CreatedAt <= shift.EndTime))
                .ToListAsync();
            ViewBag.CreatedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck).Where(t => t.ShiftId == id).ToListAsync();

            return PartialView("_ShiftDetails", shift);
        }

        // 6. NHÂN VIÊN TỰ XEM LỊCH SỬ CỦA MÌNH
        public async Task<IActionResult> MyHistory()
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var shifts = await _context.TblWorkShifts
                .Include(s => s.Staff)
                .Where(s => s.StaffId == userId)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();

            ViewBag.IsMyHistory = true; // Đánh dấu để view biết đang xem lịch sử cá nhân
            return View("Index", shifts);
        }
    }
}
