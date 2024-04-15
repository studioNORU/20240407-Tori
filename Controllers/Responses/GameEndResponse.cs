using Swashbuckle.AspNetCore.Annotations;
using Tori.Controllers.Data;

namespace tori.Controllers.Responses;

[SwaggerSchema("gameend API 응답")]
public record GameEndResponse : BaseResponse
{
    [SwaggerSchema("게임 경품 정보", Nullable = false)]
    public GameReward GameReward { get; init; } = default!;
}