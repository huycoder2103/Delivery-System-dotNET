using Microsoft.AspNetCore.Mvc;
using Delivery_System.Models;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Helpers;

namespace Delivery_System.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;

        public HomeController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var userId = User.GetUserId();

            // Lấy Ca làm việc hiện tại của User này (Status = "ACTIVE")
            var currentShift = await _context.TblWorkShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");
            
            ViewBag.CurrentShift = currentShift;

            // Lấy thống kê sơ bộ cho ca làm việc (Nếu có)
            if (currentShift != null)
            {
                // Đối với ca đang hoạt động, tính toán trực tiếp từ các đơn hàng
                // 1. Tiền cước gửi (TR) - Thu khi nhập đơn (Dựa vào ShiftId đã gắn sẵn khi tạo đơn)
                var prepaidStats = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShiftId == currentShift.ShiftId && (o.IsDeleted == false || o.IsDeleted == null))
                    .SumAsync(o => o.Tr ?? 0);

                // 2. Tiền COD (CT) - Thu khi giao đơn (Dựa vào StaffReceive và thời gian giao)
                // Lấy tất cả đơn đã giao bởi nhân viên này
                var deliveredOrders = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffReceive == userId && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null))
                    .Select(o => new { o.Ct, o.ReceiveDate })
                    .ToListAsync();

                decimal totalCod = 0;
                int deliveredCount = 0;
                var now = TimeHelper.NowVni();

                foreach (var o in deliveredOrders)
                {
                    if (DateTime.TryParseExact(o.ReceiveDate, "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dDate))
                    {
                        // Kiểm tra nếu thời gian giao nằm trong ca làm việc (từ StartTime đến hiện tại)
                        if (dDate >= currentShift.StartTime.AddMinutes(-1))
                        {
                            totalCod += o.Ct ?? 0;
                            deliveredCount++;
                        }
                    }
                }

                // Số lượng đơn đã nhập (Prepaid)
                var inputCount = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShiftId == currentShift.ShiftId && (o.IsDeleted == false || o.IsDeleted == null))
                    .CountAsync();
                
                ViewBag.DeliveredCount = deliveredCount; // Số đơn đã giao trong ca
                ViewBag.InputCount = inputCount;         // Số đơn đã nhập trong ca
                ViewBag.TotalPrepaid = prepaidStats;
                ViewBag.TotalCod = totalCod;
                ViewBag.TotalRevenue = prepaidStats + totalCod;
            }

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }

        // 2. BẮT ĐẦU CA LÀM VIỆC
        [HttpPost]
        public async Task<IActionResult> StartShift()
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var activeShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (activeShift == null)
            {
                var newShift = new TblWorkShift
                {
                    StaffId = userId,
                    StartTime = TimeHelper.NowVni(),
                    Status = "ACTIVE",
                    TotalPrepaid = 0,
                    TotalCod = 0,
                    OrderCount = 0
                };
                _context.TblWorkShifts.Add(newShift);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Bắt đầu ca làm việc thành công!";
            }
            return RedirectToAction("Index");
        }

        // 3. KẾT THÚC CA LÀM VIỆC
        [HttpPost]
        public async Task<IActionResult> EndShift()
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var activeShift = await _context.TblWorkShifts
                .FirstOrDefaultAsync(s => s.StaffId == userId && s.Status == "ACTIVE");

            if (activeShift != null)
            {
                var endTime = TimeHelper.NowVni();
                
                // TÍNH TOÁN CÁC CHỈ SỐ TRƯỚC KHI ĐÓNG CA
                // 1. Tổng TR (Cước gửi) từ các đơn đã nhập trong ca
                var totalPrepaid = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShiftId == activeShift.ShiftId && (o.IsDeleted == false || o.IsDeleted == null))
                    .SumAsync(o => o.Tr ?? 0);

                // 2. Tổng CT (COD/Cước thu khi giao) từ các đơn đã giao trong ca
                var deliveredOrders = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.StaffReceive == userId && o.ShipStatus == "Đã giao" && (o.IsDeleted == false || o.IsDeleted == null))
                    .Select(o => new { o.Ct, o.ReceiveDate })
                    .ToListAsync();

                decimal totalCod = 0;
                foreach (var o in deliveredOrders)
                {
                    if (DateTime.TryParseExact(o.ReceiveDate, "dd/MM/yyyy HH:mm", null, System.Globalization.DateTimeStyles.None, out DateTime dDate))
                    {
                        if (dDate >= activeShift.StartTime.AddMinutes(-1) && dDate <= endTime.AddMinutes(1))
                        {
                            totalCod += o.Ct ?? 0;
                        }
                    }
                }

                // 3. Tổng số đơn hàng đã nhập trong ca
                var orderCount = await _context.TblOrders.AsNoTracking()
                    .Where(o => o.ShiftId == activeShift.ShiftId && (o.IsDeleted == false || o.IsDeleted == null))
                    .CountAsync();

                // Lưu thông tin vào ca làm việc
                activeShift.Status = "ENDED";
                activeShift.EndTime = endTime;
                activeShift.TotalPrepaid = totalPrepaid;
                activeShift.TotalCod = totalCod;
                activeShift.OrderCount = orderCount;

                // TỰ ĐỘNG ĐỔ DATA VÀO BẢNG KẾ TOÁN (tblShiftAccounting)
                var accounting = new TblShiftAccounting
                {
                    ShiftId = activeShift.ShiftId,
                    SystemPrepaid = totalPrepaid,
                    SystemCod = totalCod,
                    TotalSystem = totalPrepaid + totalCod,
                    Status = 0 // Pending: Chờ kế toán xác nhận
                };
                _context.TblShiftAccountings.Add(accounting);

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã kết thúc ca làm việc và chuyển dữ liệu sang bộ phận kế toán!";
            }
            return RedirectToAction("Index");
        }
    }
}
