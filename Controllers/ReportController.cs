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

        public async Task<IActionResult> Index(string reportDate, int? viewShiftId, string targetStaffId)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var vniTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"));
            DateTime date = string.IsNullOrEmpty(reportDate) ? vniTime.Date : DateTime.Parse(reportDate);
            var selectedDate = date.Date;
            ViewBag.ReportDate = date.ToString("yyyy-MM-dd");

            if (role == "AD")
            {
                // 1. Thống kê cơ bản ngày được chọn
                ViewBag.RevenueDateVal = await _context.TblOrders.Where(o => o.CreatedAt != null && o.CreatedAt.Value.Date == selectedDate && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null)).SumAsync(o => o.Amount ?? 0);
                ViewBag.OrdersDateVal = await _context.TblOrders.CountAsync(o => o.CreatedAt != null && o.CreatedAt.Value.Date == selectedDate && (o.IsDeleted == false || o.IsDeleted == null));
                ViewBag.ActiveStaffCount = await _context.TblWorkShifts.CountAsync(s => s.Status == "ACTIVE");
                
                // 2. Dữ liệu biểu đồ doanh thu 7 ngày gần nhất
                var last7Days = Enumerable.Range(0, 7).Select(i => vniTime.Date.AddDays(-i)).OrderBy(d => d).ToList();
                var chartLabels = last7Days.Select(d => d.ToString("dd/MM")).ToList();
                var chartData = new List<decimal>();

                foreach (var d in last7Days)
                {
                    var sum = await _context.TblOrders
                        .Where(o => o.CreatedAt != null && o.CreatedAt.Value.Date == d && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null))
                        .SumAsync(o => o.Amount ?? 0);
                    chartData.Add(sum);
                }
                ViewBag.ChartLabels = chartLabels;
                ViewBag.ChartData = chartData;

                // Hiệu suất nhân viên đơn giản (không xếp hạng)
                ViewBag.StaffPerformance = await _context.TblUsers
                    .Where(u => u.RoleId == "US")
                    .Select(u => new { 
                        StaffName = u.FullName, 
                        StaffId = u.UserId, 
                        DayOrders = _context.TblOrders.Count(o => o.StaffInput == u.UserId && o.CreatedAt != null && o.CreatedAt.Value.Date == vniTime.Date),
                        DayRev = _context.TblOrders.Where(o => o.StaffInput == u.UserId && o.CreatedAt != null && o.CreatedAt.Value.Date == vniTime.Date && o.ShipStatus == "Đã chuyển").Sum(o => o.Amount ?? 0),
                        IsWorking = _context.TblWorkShifts.Any(s => s.StaffId == u.UserId && s.Status == "ACTIVE") 
                    })
                    .ToListAsync();
            }
            else
            {
                // Dữ liệu dành riêng cho nhân viên
                ViewBag.DeliveredOrders = await _context.TblOrders.CountAsync(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null));
                ViewBag.TotalRevenue = await _context.TblOrders.Where(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null)).SumAsync(o => o.Amount ?? 0);
                
                var currentShift = await _context.TblWorkShifts.FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
                ViewBag.CurrentShift = currentShift;
            }

            // --- LỊCH SỬ CA LÀM VIỆC ---
            string staffToFetch = (role == "AD" && !string.IsNullOrEmpty(targetStaffId)) ? targetStaffId : userId;
            ViewBag.SelectedStaffId = staffToFetch;

            var shiftHistory = await _context.TblWorkShifts
                .Where(s => s.StaffId == staffToFetch)
                .OrderByDescending(s => s.StartTime)
                .Take(20)
                .ToListAsync();
            ViewBag.ShiftHistory = shiftHistory;

            if (viewShiftId.HasValue)
            {
                ViewBag.ViewShiftId = viewShiftId.Value;
                ViewBag.ShiftOrders = await _context.TblOrders
                    .Where(o => o.ShiftId == viewShiftId.Value)
                    .ToListAsync();
            }

            return View();
        }
    }
}
