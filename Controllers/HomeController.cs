using Microsoft.AspNetCore.Mvc;

namespace Tori.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> logger;

    public HomeController(ILogger<HomeController> logger)
    {
        this.logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return this.Json(new
        {
            test = "Test",
        });
    }
}