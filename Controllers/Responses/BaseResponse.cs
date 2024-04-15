using Swashbuckle.AspNetCore.Annotations;

namespace tori.Controllers.Responses;

public record BaseResponse
{
    [SwaggerSchema("서버에서 응답한 시점의 UTC DateTime.Ticks입니다.", Nullable = false)]
    public long CurrentTick { get; init; }
}