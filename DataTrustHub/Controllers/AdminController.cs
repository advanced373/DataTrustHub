using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataTrustHub.Controllers;

// Administration overview; each section has its own controller under /Admin/<section>
[Authorize(Roles = Constants.AdminRole)]
public class AdminController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
