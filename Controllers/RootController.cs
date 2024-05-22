using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers;

[ApiController]
[Route("/")]
public class RootController : Controller
{
    [HttpGet("/")]
    [SwaggerOperation("HealthCheck", "AWS에서 서버 상태 체크를 위해 사용하는 API입니다.")]
    public Task<IActionResult> HealthCheck()
    {
        return Task.FromResult<IActionResult>(this.Ok());
    }
}