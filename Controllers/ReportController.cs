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

        private decimal ParseSafe(string? val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            return decimal.TryParse(val, out decimal res) ? res : 0;
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
                // Tính doanh thu hệ thống theo ngày (TR + CT)
                var dailyOrders = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow && o.ShipStatus == "Đã giao")
                    .ToListAsync();
                ViewBag.RevenueDateVal = dailyOrders.Sum(o => ParseSafe(o.Tr) + ParseSafe(o.Ct));

                ViewBag.OrdersDateVal = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= selectedDate && o.CreatedAt < tomorrow)
                    .CountAsync();

                var weekStart = vniNow.Date.AddDays(-6);
                var weekEnd = vniNow.Date.AddDays(1);
                var weeklyOrders = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.CreatedAt >= weekStart && o.CreatedAt < weekEnd && o.ShipStatus == "Đã giao")
                    .ToListAsync();

                var chartLabels = new List<string>();
                var chartData = new List<decimal>();
                for (int i = 6; i >= 0; i--)
                {
                    var d = vniNow.Date.AddDays(-i);
                    chartLabels.Add(d.ToString("dd/MM"));
                    var daySum = weeklyOrders.Where(o => o.CreatedAt!.Value.Date == d)
                        .Sum(o => ParseSafe(o.Tr) + ParseSafe(o.Ct));
                    chartData.Add(daySum);
                }
                ViewBag.ChartLabels = chartLabels;
                ViewBag.ChartData = chartData;
            }
            else
            {
                // Dữ liệu cá nhân nhân viên
                var myOrders = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffInput == userId && o.ShipStatus == "Đã giao")
                    .ToListAsync();
                
                ViewBag.DeliveredOrders = myOrders.Count;
                ViewBag.TotalRevenue = myOrders.Sum(o => ParseSafe(o.Tr) + ParseSafe(o.Ct));
                
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
