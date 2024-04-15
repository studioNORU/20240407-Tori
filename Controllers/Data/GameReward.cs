using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Data;

[SwaggerSchema("게임 경품 정보")]
public record GameReward
{
    [SwaggerSchema("게임 경품 ID", Nullable = false)]
    public string RewardId { get; init; } = default!;

    [SwaggerSchema("게임 경품 이미지 URL", Nullable = false)]
    public string RewardImage { get; init; } = default!;
}