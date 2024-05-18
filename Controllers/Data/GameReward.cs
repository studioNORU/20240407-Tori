using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Data;

[SwaggerSchema("게임 경품 정보")]
public record GameReward
{
    [SwaggerSchema("가격", Nullable = false)]
    public int Price { get; init; }
    
    [SwaggerSchema("브랜드 ID", Nullable = false)]
    public int BrandId { get; init; }

    [SwaggerSchema("상품 ID", Nullable = false)]
    public string GoodsId { get; init; } = default!;

    [SwaggerSchema("브랜드명", Nullable = false)]
    public string BrandName { get; init; } = default!;
    
    [SwaggerSchema("상품명", Nullable = false)]
    public string GoodsName { get; init; } = default!;

    [SwaggerSchema("이미지 URL", Nullable = false)]
    public string RewardImage { get; init; } = default!;
}