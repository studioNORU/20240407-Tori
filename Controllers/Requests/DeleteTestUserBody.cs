using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

#if DEBUG || DEV
[SwaggerSchema("테스트를 위한 유저 정보 삭제를 위한 데이터입니다.")]
public record struct DeleteTestUserBody
{
    [SwaggerSchema("테스트를 위한 유저의 유저 ID입니다.", Nullable = false)]
    public string UserId { get; init; }
}
#endif