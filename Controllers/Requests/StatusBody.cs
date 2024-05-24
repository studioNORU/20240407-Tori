using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

public record struct StatusBody
{
    [SwaggerSchema("유저 ID", Nullable = false)]
    public string UserId { get; init; }
}