using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Delivery_System.Controllers
{
    public class ReportController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        public ReportController(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<IActionResult> Index(string reportDate, int? viewShiftId, string targetStaffId)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var vniTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"));
            DateTime date = string.IsNullOrEmpty(reportDate) ? vniTime.Date : DateTime.Parse(reportDate);
            var selectedDate = date.Date;
            var nextDay = selectedDate.AddDays(1);
            ViewBag.ReportDate = selectedDate.ToString("yyyy-MM-dd");

            if (role == "AD")
            {
                // 1. Thống kê cơ bản ngày được chọn (Tối ưu hóa Index bằng cách lọc theo khoảng)
                ViewBag.RevenueDateVal = await _context.TblOrders
                    .AsNoTracking()
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < nextDay && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null))
                    .SumAsync(o => o.Amount ?? 0);

                ViewBag.OrdersDateVal = await _context.TblOrders
                    .AsNoTracking()
                    .CountAsync(o => o.CreatedAt >= selectedDate && o.CreatedAt < nextDay && (o.IsDeleted == false || o.IsDeleted == null));

                // Cache ActiveStaffCount (5 phút)
                if (!_cache.TryGetValue("ActiveStaffCount", out int activeStaffCount))
                {
                    activeStaffCount = await _context.TblWorkShifts.AsNoTracking().CountAsync(s => s.Status == "ACTIVE");
                    _cache.Set("ActiveStaffCount", activeStaffCount, TimeSpan.FromMinutes(5));
                }
                ViewBag.ActiveStaffCount = activeStaffCount;
                
                // 2. Tối ưu biểu đồ doanh thu 7 ngày (1 câu truy vấn thay vì 7)
                var startDate = vniTime.Date.AddDays(-6);
                var revenueByDay = await _context.TblOrders
                    .AsNoTracking()
                    .Where(o => o.CreatedAt >= startDate && o.CreatedAt < vniTime.Date.AddDays(1) && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null))
                    .GroupBy(o => o.CreatedAt.Value.Date)
                    .Select(g => new { Date = g.Key, Total = g.Sum(o => o.Amount ?? 0) })
                    .ToListAsync();

                var chartLabels = new List<string>();
                var chartData = new List<decimal>();

                for (int i = 0; i < 7; i++)
                {
                    var d = startDate.AddDays(i);
                    chartLabels.Add(d.ToString("dd/MM"));
                    var dayData = revenueByDay.FirstOrDefault(x => x.Date == d);
                    chartData.Add(dayData?.Total ?? 0);
                }

                ViewBag.ChartLabels = chartLabels;
                ViewBag.ChartData = chartData;

                // 3. Hiệu suất nhân viên
                var today = vniTime.Date;
                var nextDayToday = today.AddDays(1);
                
                // Cache StaffPerformance (5 phút)
                if (!_cache.TryGetValue("StaffPerformance", out object staffPerf))
                {
                    staffPerf = await _context.TblUsers
                        .AsNoTracking()
                        .Where(u => u.RoleId == "US")
                        .Select(u => new { 
                            StaffName = u.FullName, 
                            StaffId = u.UserId, 
                            DayOrders = _context.TblOrders.Count(o => o.StaffInput == u.UserId && o.CreatedAt >= today && o.CreatedAt < nextDayToday),
                            DayRev = _context.TblOrders.Where(o => o.StaffInput == u.UserId && o.CreatedAt >= today && o.CreatedAt < nextDayToday && o.ShipStatus == "Đã chuyển").Sum(o => o.Amount ?? 0),
                            IsWorking = _context.TblWorkShifts.Any(s => s.StaffId == u.UserId && s.Status == "ACTIVE") 
                        })
                        .ToListAsync();
                    _cache.Set("StaffPerformance", staffPerf, TimeSpan.FromMinutes(5));
                }
                ViewBag.StaffPerformance = staffPerf;
            }
            else
            {
                // Dữ liệu dành riêng cho nhân viên
                ViewBag.DeliveredOrders = await _context.TblOrders
                    .AsNoTracking()
                    .CountAsync(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null));
                
                ViewBag.TotalRevenue = await _context.TblOrders
                    .AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null))
                    .SumAsync(o => o.Amount ?? 0);
                
                ViewBag.CurrentShift = await _context.TblWorkShifts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
            }

            // --- LỊCH SỬ CA LÀM VIỆC ---
            string staffToFetch = (role == "AD" && !string.IsNullOrEmpty(targetStaffId)) ? targetStaffId : userId;
            ViewBag.SelectedStaffId = staffToFetch;

            ViewBag.ShiftHistory = await _context.TblWorkShifts
                .AsNoTracking()
                .Where(s => s.StaffId == staffToFetch)
                .OrderByDescending(s => s.StartTime)
                .Take(20)
                .ToListAsync();

            if (viewShiftId.HasValue)
            {
                ViewBag.ViewShiftId = viewShiftId.Value;
                ViewBag.ShiftOrders = await _context.TblOrders
                    .AsNoTracking()
                    .Where(o => o.ShiftId == viewShiftId.Value)
                    .ToListAsync();
            }

            return View();
        }
    }
}
