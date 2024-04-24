using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using tori.Sessions;

namespace tori.Core;

public static class JwtToken
{
    private const string UserIdKey = "userId";
    private const string NicknameKey = "nickname";
    private const string SessionIdKey = "sessionId";
    private const string SecretKey = "T1O2R3I4secret";
    
    private static readonly byte[] ByteSecretKey = Encoding.ASCII.GetBytes(SecretKey);
    private static readonly SymmetricSecurityKey SymmetricSecurityKey = new(ByteSecretKey);
    private static readonly SigningCredentials Credentials = new(SymmetricSecurityKey, SecurityAlgorithms.HmacSha256Signature);
    
    public static string ToToken(string userId, string nickname, uint sessionId)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(UserIdKey, userId),
                new Claim(NicknameKey, nickname),
                new Claim(SessionIdKey, sessionId.ToString())
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = Credentials,
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public static async Task<(bool isValid, TokenData data)> Parse(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            var result = await tokenHandler.ValidateTokenAsync(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = SymmetricSecurityKey,
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero,
            });

            if (result.IsValid || result.SecurityToken is not JwtSecurityToken jwtToken)
            {
                throw new SecurityTokenException("InvalidToken");
            }

            if (!jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256Signature,
                    StringComparison.InvariantCultureIgnoreCase))
            {
                throw new SecurityTokenException("InvalidToken");
            }

            var userId = jwtToken.Claims.First(x => x.Type == UserIdKey).Value;
            var nickname = jwtToken.Claims.First(x => x.Type == NicknameKey).Value;
            var sessionIdStr = jwtToken.Claims.First(x => x.Type == SessionIdKey).Value;

            if (!uint.TryParse(sessionIdStr, out var sessionId))
            {
                throw new SecurityTokenException("InvalidToken");
            }

            return (true, new(new UserIdentifier(userId, nickname), sessionId));
        }
        catch
        {
            return (false, default!);
        }
    }
}

public record TokenData(UserIdentifier User, uint SessionId);