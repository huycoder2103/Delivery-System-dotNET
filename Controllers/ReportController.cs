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
            var userId = HttpContext.Session.GetString("UserID") ?? "";
            var role = HttpContext.Session.GetString("Role") ?? "";

            var vniNow = Delivery_System.Helpers.TimeHelper.NowVni();
            DateTime date = string.IsNullOrEmpty(reportDate) ? vniNow.Date : DateTime.Parse(reportDate);
            var selectedDate = date.Date;
            var tomorrow = selectedDate.AddDays(1);
            ViewBag.ReportDate = date.ToString("yyyy-MM-dd");

            if (role == "AD")
            {
                // 1. Thực hiện các truy vấn tuần tự (DbContext không cho phép chạy song song trên cùng 1 instance)
                var baseQuery = _context.TblOrders.AsNoTracking();
                
                ViewBag.RevenueDateVal = await baseQuery
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow && o.ShipStatus == "Đã chuyển")
                    .SumAsync(o => o.Amount ?? 0);

                ViewBag.OrdersDateVal = await baseQuery
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow)
                    .CountAsync();

                ViewBag.ActiveStaffCount = await _context.TblWorkShifts.AsNoTracking().CountAsync(s => s.Status == "ACTIVE");
                
                var weekStart = vniNow.Date.AddDays(-6);
                var weekEnd = vniNow.Date.AddDays(1);
                var weeklyData = await baseQuery
                    .Where(o => o.CreatedAt >= weekStart && o.CreatedAt < weekEnd && o.ShipStatus == "Đã chuyển")
                    .GroupBy(o => o.CreatedAt!.Value.Date)
                    .Select(g => new { Date = g.Key, Total = g.Sum(o => o.Amount ?? 0) })
                    .ToListAsync();

                // Dữ liệu nhân viên
                var today = vniNow.Date;
                var nextDay = today.AddDays(1);
                var staffList = await _context.TblUsers.AsNoTracking().Where(u => u.RoleId == "US").Select(u => new { u.UserId, u.FullName }).ToListAsync();
                
                // Thống kê số đơn nhập (theo StaffInput)
                var inputStats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= today && o.CreatedAt < nextDay)
                    .GroupBy(o => o.StaffInput)
                    .Select(g => new { StaffId = g.Key, DayOrders = g.Count() })
                    .ToListAsync();

                // Thống kê doanh thu thực tế (theo StaffReceive - người nhận hàng tại trạm đích)
                var receiveStats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShipStatus == "Đã chuyển" && o.CreatedAt >= today && o.CreatedAt < nextDay)
                    .GroupBy(o => o.StaffReceive)
                    .Select(g => new { StaffId = g.Key, DayRev = g.Sum(o => o.Amount ?? 0) })
                    .ToListAsync();

                var workingStaffIds = await _context.TblWorkShifts.AsNoTracking().Where(s => s.Status == "ACTIVE").Select(s => s.StaffId).ToListAsync();

                var chartLabels = new List<string>();
                var chartData = new List<decimal>();
                for (int i = 6; i >= 0; i--)
                {
                    var d = vniNow.Date.AddDays(-i);
                    chartLabels.Add(d.ToString("dd/MM"));
                    chartData.Add(weeklyData.FirstOrDefault(x => x.Date == d)?.Total ?? 0);
                }
                ViewBag.ChartLabels = chartLabels;
                ViewBag.ChartData = chartData;

                var workingStaffSet = workingStaffIds.ToHashSet();
                ViewBag.StaffPerformance = staffList.Select(u => new {
                    StaffName = u.FullName ?? "N/A",
                    StaffId   = u.UserId,
                    DayOrders = inputStats.FirstOrDefault(s => s.StaffId == u.UserId)?.DayOrders ?? 0,
                    DayRev    = receiveStats.FirstOrDefault(s => s.StaffId == u.UserId)?.DayRev ?? 0,
                    IsWorking = workingStaffSet.Contains(u.UserId)
                }).ToList();
            }
            else
            {
                // Dữ liệu dành riêng cho nhân viên: Tính dựa trên StaffReceive (Hàng đã nhận tại trạm mình quản lý)
                var userStats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffReceive == userId && o.ShipStatus == "Đã chuyển")
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