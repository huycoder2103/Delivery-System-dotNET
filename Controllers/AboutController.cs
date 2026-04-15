using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delivery_System.Models;

namespace Delivery_System.Controllers
{
    public class AboutController : Controller
    {
        private readonly AppDbContext _context;

        public AboutController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var stationList = await _context.TblStations
                .AsNoTracking()
                .Where(s => s.IsActive == true)
                .OrderBy(s => s.StationId)
                .ToListAsync();

            ViewBag.StationList = stationList;
            return View();
        }
    }
}
