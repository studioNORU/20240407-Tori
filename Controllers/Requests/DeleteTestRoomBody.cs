using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

#if DEBUG || DEV
[SwaggerSchema("테스트를 위한 방 정보 삭제를 위한 데이터입니다.")]
public record struct DeleteTestRoomBody
{
    [SwaggerSchema("테스트를 위한 방의 방 ID입니다.", Nullable = false)]
    public int RoomId { get; init; }
}
#endif