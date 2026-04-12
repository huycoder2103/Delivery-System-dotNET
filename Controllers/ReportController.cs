using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;

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
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");

            var vniTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"));
            DateTime date = string.IsNullOrEmpty(reportDate) ? vniTime.Date : DateTime.Parse(reportDate);
            var selectedDate = date.Date;
            var tomorrow = selectedDate.AddDays(1);
            ViewBag.ReportDate = date.ToString("yyyy-MM-dd");

            if (role == "AD")
            {
                // 1. Thống kê cơ bản ngày được chọn (Tối ưu: AsNoTracking & Range Search)
                var baseQuery = _context.TblOrders.AsNoTracking().Where(o => o.IsDeleted == false || o.IsDeleted == null);
                
                ViewBag.RevenueDateVal = await baseQuery
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow && o.ShipStatus == "Đã chuyển")
                    .SumAsync(o => o.Amount ?? 0);

                ViewBag.OrdersDateVal = await baseQuery
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow)
                    .CountAsync();

                ViewBag.ActiveStaffCount = await _context.TblWorkShifts.AsNoTracking().CountAsync(s => s.Status == "ACTIVE");
                
                // 2. Dữ liệu biểu đồ doanh thu 7 ngày gần nhất (Tối ưu: Gộp 7 query thành 1)
                var weekStart = vniTime.Date.AddDays(-6);
                var weekEnd = vniTime.Date.AddDays(1);

                var weeklyData = await baseQuery
                    .Where(o => o.CreatedAt >= weekStart && o.CreatedAt < weekEnd && o.ShipStatus == "Đã chuyển")
                    .GroupBy(o => o.CreatedAt!.Value.Date)
                    .Select(g => new { Date = g.Key, Total = g.Sum(o => o.Amount ?? 0) })
                    .ToListAsync();

                var chartLabels = new List<string>();
                var chartData = new List<decimal>();

                for (int i = 6; i >= 0; i--)
                {
                    var d = vniTime.Date.AddDays(-i);
                    chartLabels.Add(d.ToString("dd/MM"));
                    chartData.Add(weeklyData.FirstOrDefault(x => x.Date == d)?.Total ?? 0);
                }
                ViewBag.ChartLabels = chartLabels;
                ViewBag.ChartData = chartData;

                // 3. Hiệu suất nhân viên (Tối ưu: Join một lần thay vì N+1 query)
                var today = vniTime.Date;
                var nextDay = today.AddDays(1);

                var staffIds = await _context.TblUsers.AsNoTracking()
                    .Where(u => u.RoleId == "US")
                    .Select(u => new { u.UserId, u.FullName })
                    .ToListAsync();

                var orderStats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= today && o.CreatedAt < nextDay 
                             && (o.IsDeleted == false || o.IsDeleted == null))
                    .GroupBy(o => o.StaffInput)
                    .Select(g => new {
                        StaffId  = g.Key,
                        DayOrders = g.Count(),
                        DayRev    = g.Where(o => o.ShipStatus == "Đã chuyển").Sum(o => o.Amount ?? 0)
                    }).ToListAsync();

                var workingStaffList = await _context.TblWorkShifts.AsNoTracking()
                    .Where(s => s.Status == "ACTIVE")
                    .Select(s => s.StaffId)
                    .ToListAsync();
                var workingStaffSet = workingStaffList.ToHashSet();

                ViewBag.StaffPerformance = staffIds.Select(u => new {
                    StaffName = u.FullName,
                    StaffId   = u.UserId,
                    DayOrders = orderStats.FirstOrDefault(s => s.StaffId == u.UserId)?.DayOrders ?? 0,
                    DayRev    = orderStats.FirstOrDefault(s => s.StaffId == u.UserId)?.DayRev ?? 0,
                    IsWorking = workingStaffSet.Contains(u.UserId)
                }).ToList();
            }
            else
            {
                // Dữ liệu dành riêng cho nhân viên (Tối ưu: AsNoTracking & GroupBy)
                var userStats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển")
                    .GroupBy(o => 1)
                    .Select(g => new { 
                        Count = g.Count(), 
                        Sum = g.Sum(o => o.Amount ?? 0) 
                    })
                    .FirstOrDefaultAsync();
                
                ViewBag.DeliveredOrders = userStats?.Count ?? 0;
                ViewBag.TotalRevenue = userStats?.Sum ?? 0;
                
                ViewBag.CurrentShift = await _context.TblWorkShifts.AsNoTracking().FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
            }

            // --- LỊCH SỬ CA LÀM VIỆC ---
            string staffToFetch = (role == "AD" && !string.IsNullOrEmpty(targetStaffId)) ? targetStaffId : userId;
            ViewBag.SelectedStaffId = staffToFetch;

            ViewBag.ShiftHistory = await _context.TblWorkShifts.AsNoTracking()
                .Where(s => s.StaffId == staffToFetch)
                .OrderByDescending(s => s.StartTime)
                .Take(20)
                .ToListAsync();

            if (viewShiftId.HasValue)
            {
                ViewBag.ViewShiftId = viewShiftId.Value;
                ViewBag.ShiftOrders = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShiftId == viewShiftId.Value)
                    .ToListAsync();
            }

            return View();
        }
    }
}