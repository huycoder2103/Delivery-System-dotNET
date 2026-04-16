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
                // Dữ liệu cá nhân nhân viên: Tính toán tại Database
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
