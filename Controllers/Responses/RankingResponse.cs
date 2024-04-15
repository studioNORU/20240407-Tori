using Swashbuckle.AspNetCore.Annotations;
using Tori.Controllers.Data;

namespace tori.Controllers.Responses;

[SwaggerSchema("ranking, result API 응답")]
public record RankingResponse : BaseResponse
{
    [SwaggerSchema("내 정보", Nullable = false)]
    public RankInfo MyRank { get; init; } = default!;

    [SwaggerSchema("1등 정보", Nullable = false)]
    public RankInfo TopRank { get; init; } = default!;
}