using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Delivery_System.Helpers;

namespace Delivery_System.Controllers
{
    public class ReportController : Controller
    {
        private readonly AppDbContext _context;

        public ReportController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? reportDate, int? viewShiftId, string? targetStaffId)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            var vniNow = TimeHelper.NowVni();
            DateTime date = string.IsNullOrEmpty(reportDate) ? vniNow.Date : DateTime.Parse(reportDate);
            var selectedDate = date.Date;
            var tomorrow = selectedDate.AddDays(1);
            ViewBag.ReportDate = date.ToString("yyyy-MM-dd");

            if (role == "AD")
            {
                // Tính doanh thu hệ thống theo ngày (TR + CT) trực tiếp tại Database
                ViewBag.RevenueDateVal = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow && o.ShipStatus == "Đã giao")
                    .SumAsync(o => (o.Tr ?? 0) + (o.Ct ?? 0));

                ViewBag.OrdersDateVal = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow)
                    .CountAsync();

                var weekStart = vniNow.Date.AddDays(-6);
                var weekEnd = vniNow.Date.AddDays(1);
                
                // Lấy dữ liệu biểu đồ tuần: Chỉ lấy tổng theo ngày, không lấy cả danh sách đơn hàng
                var weeklyData = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= weekStart && o.CreatedAt < weekEnd && o.ShipStatus == "Đã giao")
                    .GroupBy(o => o.CreatedAt!.Value.Date)
                    .Select(g => new { Day = g.Key, Total = g.Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0)) })
                    .ToListAsync();

                var chartLabels = new List<string>();
                var chartData = new List<decimal>();
                for (int i = 6; i >= 0; i--)
                {
                    var d = vniNow.Date.AddDays(-i);
                    chartLabels.Add(d.ToString("dd/MM"));
                    var dayTotal = weeklyData.FirstOrDefault(x => x.Day == d)?.Total ?? 0;
                    chartData.Add(dayTotal);
                }
                ViewBag.ChartLabels = chartLabels;
                ViewBag.ChartData = chartData;
            }
            else
            {
                // DỮ LIỆU CÁ NHÂN NHÂN VIÊN
                
                // 1. Doanh thu theo ngày đã chọn (mặc định hôm nay)
                ViewBag.RevenueDateVal = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow && o.ShipStatus == "Đã giao")
                    .SumAsync(o => (o.Tr ?? 0) + (o.Ct ?? 0));

                ViewBag.OrdersDateVal = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow && o.ShipStatus == "Đã giao")
                    .CountAsync();

                // 2. Tổng doanh thu tích lũy từ trước đến nay
                var stats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.ShipStatus == "Đã giao")
                    .GroupBy(o => 1)
                    .Select(g => new { 
                        Count = g.Count(), 
                        Revenue = g.Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0)) 
                    })
                    .FirstOrDefaultAsync();
                
                ViewBag.DeliveredOrders = stats?.Count ?? 0;
                ViewBag.TotalRevenue = stats?.Revenue ?? 0;
                
                ViewBag.CurrentShift = await _context.TblWorkShifts.AsNoTracking().FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
            }

            // --- LỊCH SỬ CA LÀM VIỆC (CHO TAB NHẬT KÝ) ---
            string staffToFetch = (role == "AD" && !string.IsNullOrEmpty(targetStaffId)) ? targetStaffId : userId;
            ViewBag.SelectedStaffId = staffToFetch;

            var shiftQuery = _context.TblWorkShifts.Include(s => s.Staff).AsQueryable();
            if (role != "AD") 
            {
                shiftQuery = shiftQuery.Where(s => s.StaffId == userId);
            }
            else if (!string.IsNullOrEmpty(targetStaffId))
            {
                shiftQuery = shiftQuery.Where(s => s.StaffId == targetStaffId);
            }

            var shifts = await shiftQuery.OrderByDescending(s => s.StartTime).Take(50).ToListAsync();

            // Tính toán doanh thu cho từng ca giống bên ShiftController
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();
            var shiftRevenues = await _context.TblOrders.AsNoTracking()
                .Where(o => o.ShiftId.HasValue && shiftIds.Contains(o.ShiftId.Value) && o.ShipStatus == "Đã giao")
                .GroupBy(o => o.ShiftId)
                .Select(g => new { ShiftId = g.Key, Total = g.Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0)) })
                .ToDictionaryAsync(x => x.ShiftId, x => x.Total);

            foreach (var s in shifts)
            {
                s.Revenue = shiftRevenues.ContainsKey(s.ShiftId) ? shiftRevenues[s.ShiftId] : 0;
            }

            ViewBag.ShiftHistory = shifts;

            return View();
        }

        // --- CHI TIẾT HOẠT ĐỘNG TRONG CA ---
        public async Task<IActionResult> Details(int id)
        {
            var userId = User.GetUserId();
            var role = User.GetRole();

            var shift = await _context.TblWorkShifts
                .Include(s => s.Staff)
                .FirstOrDefaultAsync(s => s.ShiftId == id);

            if (shift == null) return NotFound();

            if (role != "AD" && shift.StaffId != userId) return Forbid();

            var addedOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.ShiftId == id && o.StaffInput == shift.StaffId)
                .ToListAsync();
            
            ViewBag.AddedOrders = addedOrders;
            ViewBag.TotalRevenue = addedOrders
                .Where(o => o.ShipStatus == "Đã giao")
                .Sum(o => (o.Tr ?? 0) + (o.Ct ?? 0));

            ViewBag.ReceivedOrders = await _context.TblOrders.AsNoTracking()
                .Where(o => o.StaffReceive == shift.StaffId && o.ShipStatus == "Đã giao" 
                            && o.CreatedAt >= shift.StartTime && (shift.EndTime == null || o.CreatedAt <= shift.EndTime))
                .ToListAsync();

            ViewBag.CreatedTrips = await _context.TblTrips.AsNoTracking().Include(t => t.Truck).Where(t => t.ShiftId == id).ToListAsync();

            return PartialView("_ShiftDetails", shift);
        }

        // --- ADMIN KẾT THÚC HỘ CA LÀM VIỆC ---
        [HttpPost]
        public async Task<IActionResult> ForceEnd(int shiftId)
        {
            if (User.GetRole() != "AD") return Forbid();

            var shift = await _context.TblWorkShifts.FindAsync(shiftId);
            if (shift != null && shift.Status == "ACTIVE")
            {
                shift.Status = "ENDED";
                shift.EndTime = TimeHelper.NowVni();
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã kết thúc ca làm việc của nhân viên!";
            }
            return RedirectToAction("Index");
        }
    }
}
