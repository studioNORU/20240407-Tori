using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tori.Core;

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
    [Route("loading")]
    [SwaggerOperation("로딩", "게임 진입에 앞서 필요한 정보를 로딩합니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "올바르지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "이미 해당 유저가 게임에 참여 중이기 때문에 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(LoadingResponse))]
    public async Task<IActionResult> Loading(
        [FromForm, SwaggerSchema("플레이어 ID")] string userId,
        [FromForm, SwaggerSchema("플레이어 닉네임")] string userNickname)
    {
        if (string.IsNullOrWhiteSpace(userId)) return this.BadRequest();
        if (string.IsNullOrWhiteSpace(userNickname)) return this.BadRequest();

        return this.Json(new LoadingResponse
        {
            Token = "asd",
            Constants = new(),
            StageId = "test",
            GameStartUtc = DateTime.UtcNow.Ticks,
            GameEndUtc = DateTime.UtcNow.Ticks,
        });
    }

    private record LoadingResponse
    {
        [SwaggerSchema("API 사용에 필요한 토큰입니다. 사용자를 구분하기 위한 값입니다.", Nullable = false)]
        public string Token { get; init; } = default!;
        
        [SwaggerSchema("게임에 관련된 상수들입니다.", Nullable = false)]
        public Dictionary<string, int> Constants { get; init; } = default!;
        
        [SwaggerSchema("플레이어가 속한 스테이지의 ID입니다.", Nullable = false)]
        public string StageId { get; init; } = default!;
        
        [SwaggerSchema("게임 시작 시간에 대한 C# DateTime.Ticks입니다.", Nullable = false)]
        public long GameStartUtc { get; init; }
        
        [SwaggerSchema("게임 종료 시간에 대한 C# DateTime.Ticks입니다.", Nullable = false)]
        public long GameEndUtc { get; init; }
    }
}