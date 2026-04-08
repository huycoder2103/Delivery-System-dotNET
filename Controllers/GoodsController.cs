using Microsoft.AspNetCore.Mvc;

namespace Delivery_System.Controllers
{
    public class GoodsController : Controller
    {
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetString("UserID");
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            return View();
        }
    }
}
