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

            DateTime date = string.IsNullOrEmpty(reportDate) ? DateTime.Today : DateTime.Parse(reportDate);
            string dateStr = date.ToString("dd/MM/yyyy");
            ViewBag.ReportDate = date.ToString("yyyy-MM-dd");

            if (role == "AD")
            {
                ViewBag.RevenueDateVal = await _context.TblOrders.Where(o => o.ReceiveDate == dateStr && o.ShipStatus == "Đã chuyển" && (o.IsDeleted == false || o.IsDeleted == null)).SumAsync(o => o.Amount ?? 0);
                ViewBag.OrdersDateVal = await _context.TblOrders.CountAsync(o => o.ReceiveDate == dateStr && (o.IsDeleted == false || o.IsDeleted == null));
                ViewBag.ActiveStaffCount = await _context.TblWorkShifts.CountAsync(s => s.Status == "ACTIVE");
                
                // Hiệu suất nhân viên
                ViewBag.StaffPerformance = await _context.TblUsers
                    .Select(u => new { 
                        StaffName = u.FullName, 
                        StaffId = u.UserId, 
                        TotalOrders = _context.TblOrders.Count(o => o.StaffInput == u.UserId && o.ReceiveDate == dateStr), 
                        TotalRevenue = _context.TblOrders.Where(o => o.StaffInput == u.UserId && o.ReceiveDate == dateStr && o.ShipStatus == "Đã chuyển").Sum(o => o.Amount ?? 0), 
                        IsWorking = _context.TblWorkShifts.Any(s => s.StaffId == u.UserId && s.Status == "ACTIVE") 
                    }).ToListAsync();
            }
            else
            {
                var currentShift = await _context.TblWorkShifts.FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
                ViewBag.CurrentShift = currentShift;
                if (currentShift != null)
                {
                    ViewBag.DeliveredOrders = await _context.TblOrders.CountAsync(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển");
                    ViewBag.TotalRevenue = await _context.TblOrders.Where(o => o.StaffInput == userId && o.ShipStatus == "Đã chuyển").SumAsync(o => o.Amount ?? 0);
                }
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
