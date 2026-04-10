using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;
using Delivery_System.Helpers;
using Microsoft.Extensions.Caching.Memory;

namespace Delivery_System.Controllers
{
    public class OrderController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        public OrderController(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // 1. Danh sách đơn hàng (Hỗ trợ lọc và phân trang)
        [HttpGet]
        public async Task<IActionResult> List(string sendStationFilter, string receiveStationFilter, string searchPhone, string dateFilter, string statusFilter = "all", int page = 1)
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            int pageSize = 20;
            ViewBag.StationList = await _context.TblStations.Where(s => s.IsActive == true).AsNoTracking().ToListAsync();
            var query = _context.TblOrders.Where(o => o.IsDeleted == false || o.IsDeleted == null).AsQueryable();

            // Phân loại hàng
            if (statusFilter == "pending") query = query.Where(o => string.IsNullOrEmpty(o.TripId));
            else if (statusFilter == "shipped") query = query.Where(o => !string.IsNullOrEmpty(o.TripId));

            if (!string.IsNullOrEmpty(sendStationFilter)) query = query.Where(o => o.SendStation == sendStationFilter);
            if (!string.IsNullOrEmpty(receiveStationFilter)) query = query.Where(o => o.ReceiveStation == receiveStationFilter);
            if (!string.IsNullOrEmpty(searchPhone)) query = query.Where(o => o.SenderPhone.Contains(searchPhone) || o.ReceiverPhone.Contains(searchPhone) || o.OrderId.Contains(searchPhone));
            
            if (!string.IsNullOrEmpty(dateFilter) && DateTime.TryParse(dateFilter, out DateTime dt))
            {
                query = query.Where(o => o.CreatedAt != null && o.CreatedAt.Value.Date == dt.Date);
            }

            // Tính toán phân trang
            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            page = page < 1 ? 1 : (page > totalPages && totalPages > 0 ? totalPages : page);

            var list = await query.OrderByDescending(o => o.OrderId)
                                 .Skip((page - 1) * pageSize)
                                 .Take(pageSize)
                                 .AsNoTracking()
                                 .Select(o => new TblOrder {
                                     OrderId = o.OrderId,
                                     ItemName = o.ItemName,
                                     SendStation = o.SendStation,
                                     ReceiveStation = o.ReceiveStation,
                                     SenderName = o.SenderName,
                                     SenderPhone = o.SenderPhone,
                                     ReceiverName = o.ReceiverName,
                                     ReceiverPhone = o.ReceiverPhone,
                                     StaffInput = o.StaffInput,
                                     Tr = o.Tr,
                                     Ct = o.Ct,
                                     Note = o.Note,
                                     CreatedAt = o.CreatedAt,
                                     TripId = o.TripId
                                 })
                                 .ToListAsync();
            
            ViewBag.TotalPages = totalPages;
            ViewBag.CurrentPage = page;
            ViewBag.TotalItems = totalItems;
            ViewBag.SearchPhone = searchPhone;
            ViewBag.CurrentStatus = statusFilter;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                // Trả về cả danh sách và thông tin phân trang cho AJAX
                return Json(new { 
                    html = await this.RenderViewAsync("_OrderTableBody", list, true),
                    totalPages = totalPages,
                    currentPage = page,
                    totalItems = totalItems
                });
            }

            return View(list);
        }

        // 2. Trang chọn chuyến xe để chuyển hàng
        [HttpGet]
        public async Task<IActionResult> Ship(string id)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();

            if (role != "AD" && order.StaffInput != userId)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền chuyển đơn hàng này!";
                return RedirectToAction("List");
            }

            var matchingTrips = await _context.VwTripLists
                .Where(t => t.Departure == order.SendStation && t.Status == "Đang đi")
                .AsNoTracking().ToListAsync();

            ViewBag.OrderForShip = order;
            return View(matchingTrips);
        }

        // 3. Xử lý gán đơn hàng vào chuyến xe
        [HttpPost]
        public async Task<IActionResult> AssignToTrip(string orderId, string tripId, string source)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");

            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == orderId);
            if (order != null)
            {
                if (role != "AD" && order.StaffInput != userId)
                {
                    TempData["ErrorMessage"] = "Hành động bị từ chối do thiếu quyền hạn!";
                    return RedirectToAction("List");
                }

                order.TripId = tripId;
                order.ShipStatus = "Đang chuyển";
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Gán đơn hàng {orderId} lên chuyến {tripId} thành công!";
            }

            if (source == "ship") return RedirectToAction("List");
            return RedirectToAction("List", "Trip");
        }

        // 4. Trang chỉnh sửa đơn hàng
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order == null) return NotFound();

            if (role != "AD" && order.StaffInput != userId)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền sửa đơn hàng do người khác tạo!";
                return RedirectToAction("List");
            }
            
            ViewBag.StationList = await _context.TblStations.Where(s => s.IsActive == true).AsNoTracking().ToListAsync();
            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(TblOrder order)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");

            var existing = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == order.OrderId);
            if (existing == null) return NotFound();

            if (role != "AD" && existing.StaffInput != userId)
            {
                TempData["ErrorMessage"] = "Hành động bị từ chối do thiếu quyền hạn!";
                return RedirectToAction("List");
            }

            existing.ItemName = order.ItemName;
            existing.SendStation = order.SendStation;
            existing.ReceiveStation = order.ReceiveStation;
            existing.SenderName = order.SenderName;
            existing.SenderPhone = order.SenderPhone;
            existing.ReceiverName = order.ReceiverName;
            existing.ReceiverPhone = order.ReceiverPhone;
            existing.Amount = order.Amount;
            existing.Tr = order.Tr;
            existing.Ct = order.Ct;
            existing.Note = order.Note;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Cập nhật đơn hàng thành công!";
            return RedirectToAction("List");
        }

        // 6. Xóa tạm thời (vào thùng rác)
        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var userId = HttpContext.Session.GetString("UserID");
            var role = HttpContext.Session.GetString("Role");

            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null)
            {
                if (role != "AD" && order.StaffInput != userId)
                {
                    TempData["ErrorMessage"] = "Bạn không có quyền xóa đơn hàng này!";
                    return RedirectToAction("List");
                }

                order.IsDeleted = true;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã đưa đơn hàng vào thùng rác!";
            }
            return RedirectToAction("List");
        }

        // 7. Tạo đơn hàng mới
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            if (!_cache.TryGetValue("StationList", out List<TblStation> stations))
            {
                stations = await _context.TblStations.Where(s => s.IsActive == true).AsNoTracking().ToListAsync();
                _cache.Set("StationList", stations, TimeSpan.FromMinutes(30));
            }
            ViewBag.StationList = stations;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(TblOrder order)
        {
            var vniTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"));

            // Thuật toán tạo mã ORDER-000001 tăng dần
            string newOrderId = "ORDER-000001";
            var lastOrder = await _context.TblOrders
                .Where(o => o.OrderId.StartsWith("ORDER-"))
                .OrderByDescending(o => o.OrderId)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (lastOrder != null)
            {
                // Tách phần số từ chuỗi "ORDER-XXXXXX"
                string lastIdNumberPart = lastOrder.OrderId.Replace("ORDER-", "");
                if (int.TryParse(lastIdNumberPart, out int lastIdNumber))
                {
                    newOrderId = "ORDER-" + (lastIdNumber + 1).ToString("D6");
                }
            }

            order.OrderId = newOrderId;
            order.StaffInput = HttpContext.Session.GetString("UserID");
            order.ReceiveDate = null;
            order.ShipStatus = "Chưa Chuyển";
            order.IsDeleted = false;
            order.CreatedAt = vniTime;
            
            _context.TblOrders.Add(order);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Thêm đơn hàng {newOrderId} thành công!";
            return RedirectToAction("List");
        }

        // 8. Thùng rác
        [HttpGet]
        public async Task<IActionResult> Trash()
        {
            var list = await _context.TblOrders.Where(o => o.IsDeleted == true).AsNoTracking().ToListAsync();
            return View(list);
        }

        [HttpPost]
        public async Task<IActionResult> Restore(string id)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null)
            {
                order.IsDeleted = false;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Khôi phục đơn hàng thành công!";
            }
            return RedirectToAction("Trash");
        }

        [HttpPost]
        public async Task<IActionResult> HardDelete(string id)
        {
            var order = await _context.TblOrders.FirstOrDefaultAsync(o => o.OrderId == id);
            if (order != null)
            {
                _context.TblOrders.Remove(order);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã xóa vĩnh viễn đơn hàng!";
            }
            return RedirectToAction("Trash");
        }

        // 13. In biên nhận đơn hàng
        [HttpGet]
        public async Task<IActionResult> PrintReceipt(string id)
        {
            var order = await _context.TblOrders.FindAsync(id);
            if (order == null) return NotFound();
            return View(order);
        }
    }
}
