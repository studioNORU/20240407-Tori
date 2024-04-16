﻿using Swashbuckle.AspNetCore.Annotations;

namespace Tori.Controllers.Requests;

[SwaggerSchema("인증을 위한 게임 토큰을 요구합니다.")]
public record struct AuthBody : IAuthBody
{
    [SwaggerSchema("게임 토큰", Nullable = false)]
    public string Token { get; init; }
}