using Microsoft.AspNetCore.Mvc;
using Delivery_System.Helpers;

namespace Delivery_System.Controllers
{
    public class GoodsController : Controller
    {
        public IActionResult Index()
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId)) return RedirectToAction("Login", "Account");

            return View();
        }
    }
}
