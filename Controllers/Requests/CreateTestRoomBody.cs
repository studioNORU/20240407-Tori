using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

#if !RELEASE
[SwaggerSchema("테스트를 위한 방 정보 등록을 위한 데이터입니다.")]
public record struct CreateTestRoomBody
{
    [SwaggerSchema("테스트를 위한 방의 방 ID입니다. 이후 다른 API에서 이 방 ID를 사용할 경우, 앱 서버를 거치지 않고 테스트를 위한 방 정보를 사용합니다.", Nullable = false)]
    public int RoomId { get; init; }
    
    [SwaggerSchema("테스트를 위한 방의 게임 시작 시간입니다. 이 시간 전까지 loading API를 진행하고, 이 시간 이후로 gamestart API를 진행할 수 있습니다.", Nullable = false)]
    public long GameStartUtc { get; init; }
    
    [SwaggerSchema("테스트를 위한 방의 게임 종료 시간입니다. 이 시간 전까지 gamestart API를 진행하고, 이 시간 이후로 gameend API나 result API를 진행할 수 있습니다.", Nullable = false)]
    public long GameEndUtc { get; init; }
    
    [SwaggerSchema("이 테스트를 위한 방 정보가 만료될 시간입니다. 이 시간 이후로는 이 정보가 게임 서버에서 사용되지 않습니다.", Nullable = false)]
    public long ExpireAtUtc { get; init; }
}
#endif