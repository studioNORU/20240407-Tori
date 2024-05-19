using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

public record struct StatusBody
{
    [SwaggerSchema("플레이어 ID", Nullable = false)]
    public string UserId { get; init; }
}