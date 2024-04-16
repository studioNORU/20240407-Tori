using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Data;

[SwaggerSchema("랭킹 정보")]
public record RankInfo
{
    [SwaggerSchema("플레이어 ID", Nullable = false)]
    public string UserId { get; init; } = default!;

    [SwaggerSchema("플레이어 닉네임", Nullable = false)]
    public string UserNickname { get; init; } = default!;
    
    [SwaggerSchema("랭킹 등수", Nullable = false)]
    public int Ranking { get; init; }
    
    [SwaggerSchema("호스트 시간 (점수)", Nullable = false)]
    public int HostTime { get; init; }
}