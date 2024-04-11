using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers;

[ApiController]
[Route("[controller]")]
public class ToriController : Controller
{
    private readonly ILogger<ToriController> logger;

    public ToriController(ILogger<ToriController> logger)
    {
        this.logger = logger;
    }

    [HttpPost]
    [Route("join")]
    [SwaggerOperation("게임 방 참가 요청", "게임 방 참가를 요청합니다. 참가 가능한 방이 없으면 새로 만들어 참가하게 됩니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Wrong Parameters")]
    public async Task<IActionResult> Join(
        [FromForm, SwaggerSchema("플레이어 ID")] string playerId,
        [FromForm, SwaggerSchema("플레이어 닉네임")] string playerNickname)
    {
        if (string.IsNullOrWhiteSpace(playerId)) return this.BadRequest();
        if (string.IsNullOrWhiteSpace(playerNickname)) return this.BadRequest();

        return this.Json(new { sessionId = "" });
    }
}