using Microsoft.AspNetCore.Mvc;

namespace Delivery_System.Controllers
{
    public class AboutController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
