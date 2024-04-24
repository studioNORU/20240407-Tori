using Swashbuckle.AspNetCore.Annotations;

namespace tori.Controllers.Responses;

[SwaggerSchema("gameend API 응답")]
public record GameEndResponse : BaseResponse
{
    
}