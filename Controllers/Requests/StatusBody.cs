using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

public record struct StatusBody
{
    [SwaggerSchema("클라이언트 앱 버전", Nullable = false)]
    public string ClientVersion { get; init; }
    
    [SwaggerSchema("유저 ID", Nullable = false)]
    public string UserId { get; init; }
}