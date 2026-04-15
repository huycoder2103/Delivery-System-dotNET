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

        private decimal ParseSafe(string? val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            return decimal.TryParse(val, out decimal res) ? res : 0;
        }

        // 1. TRANG QUẢN LÝ CA LÀM VIỆC (DÀNH CHO ADMIN)
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin()) return RedirectToAction("Index", "Home");
            var shifts = await GetShiftsWithRevenueAsync(null);
            return View(shifts);
        }

        private async Task<List<TblWorkShift>> GetShiftsWithRevenueAsync(string? userId)
        {
            var query = _context.TblWorkShifts.Include(s => s.Staff).AsQueryable();
            if (!string.IsNullOrEmpty(userId))
            {
                query = query.Where(s => s.StaffId == userId);
            }

            var shifts = await query.OrderByDescending(s => s.StartTime).ToListAsync();

            // Tính toán doanh thu cho từng ca
            foreach (var s in shifts)
            {
                var orders = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShiftId == s.ShiftId && o.StaffInput == s.StaffId && o.ShipStatus == "Đã giao")
                    .ToListAsync();
                
                s.Revenue = orders.Sum(o => ParseSafe(o.Tr) + ParseSafe(o.Ct));
            }

            return shifts;
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

        // 5. CHI TIẾT HOẠT ĐỘNG TRONG CA (ADMIN HOẶC NHÂN VIÊN TỰ XEM)
        public async Task<IActionResult> Details(int id)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");

            var shift = await _context.TblWorkShifts
                .Include(s => s.Staff)
                .FirstOrDefaultAsync(s => s.ShiftId == id);

            if (shift == null) return NotFound();

            // Chỉ Admin hoặc chính nhân viên đó mới được xem
            if (role != "AD" && shift.StaffId != userId) return Forbid();

            // Lấy danh sách hàng hóa do nhân viên này NHẬP trong ca này
            var addedOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.ShiftId == id && o.StaffInput == shift.StaffId)
                .ToListAsync();
            
            ViewBag.AddedOrders = addedOrders;
            // Doanh thu tính cho người NHẬP đơn (TR + CT) cho những đơn đã giao
            ViewBag.TotalRevenue = addedOrders
                .Where(o => o.ShipStatus == "Đã giao")
                .Sum(o => ParseSafe(o.Tr) + ParseSafe(o.Ct));

            // Lấy danh sách hàng hóa nhân viên này thực hiện GIAO (xác nhận đến) trong ca này
            // Lưu ý: Phần này để theo dõi công việc vận chuyển, không tính vào doanh thu cá nhân của họ nếu họ không phải người nhập.
            ViewBag.ReceivedOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.StaffReceive == shift.StaffId && o.ShipStatus == "Đã giao" 
                            && o.CreatedAt >= shift.StartTime && (shift.EndTime == null || o.CreatedAt <= shift.EndTime))
                .ToListAsync();

            // Chuyến xe do nhân viên này tạo
            ViewBag.CreatedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck).Where(t => t.ShiftId == id).ToListAsync();

            return PartialView("_ShiftDetails", shift);
        }

        // 6. NHÂN VIÊN TỰ XEM LỊCH SỬ CỦA MÌNH
        public async Task<IActionResult> MyHistory()
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var shifts = await GetShiftsWithRevenueAsync(userId);

            ViewBag.IsMyHistory = true; // Đánh dấu để view biết đang xem lịch sử cá nhân
            return View("Index", shifts);
        }
    }
}
