using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Tori.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> logger;

    public HomeController(ILogger<HomeController> logger)
    {
        this.logger = logger;
    }

    public IActionResult Index()
    {
        return this.Json(new
        {
            test = "Test",
        });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        this.logger.Log(LogLevel.Error, "REQ : {reqId}", Activity.Current?.Id ?? this.HttpContext.TraceIdentifier);
        return this.Problem();
    }
}