using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Tori.Controllers.Data;
using Tori.Controllers.Requests;
using tori.Controllers.Responses;
using tori.Core;
using tori.Sessions;

namespace Tori.Controllers;

[ApiController]
[Route("[controller]")]
public class ToriController : Controller
{
    private readonly ILogger<ToriController> logger;

    public ToriController(ILogger<ToriController> logger)
    {
        this.logger = logger;
    }

    [HttpPost]
    [Route("loading")]
    [SwaggerOperation("로딩", "게임 진입에 앞서 필요한 정보를 로딩합니다. (WIP)")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "이미 해당 유저가 게임에 참여 중이기 때문에 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(LoadingResponse))]
    public async Task<IActionResult> Loading([FromBody] LoadingBody req)
    {
        if (string.IsNullOrWhiteSpace(req.UserId)) return this.BadRequest();
        if (string.IsNullOrWhiteSpace(req.UserNickname)) return this.BadRequest();

        try
        {
            var resultCode =
                SessionManager.I.TryJoin(new UserIdentifier(req.UserId, req.UserNickname), out var session);
            
            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;
                
                case ResultCode.AlreadyJoined:
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }
            
            return this.Json(new LoadingResponse
            {
                Token = JwtToken.ToToken(req.UserId, req.UserNickname, session.SessionId),
                Constants = new(),
                StageId = session.StageId,
                GameStartUtc = session.GameStartAt.Ticks,
                GameEndUtc = session.GameEndAt.Ticks,
                CurrentTick = DateTime.UtcNow.Ticks,
            });
        }
        catch (Exception e)
        {
            this.logger.LogCritical(e, "API HAS EXCEPTION - loading [userId : {userId}, userNickname : {userNickname}]",
                req.UserId, req.UserNickname);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<(ResultCode, SessionUser?)> ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return (ResultCode.InvalidParameter, null);
        
        var (isValid, data) = await JwtToken.Parse(token);
        if (!isValid || string.IsNullOrWhiteSpace(data.User.Id)) return (ResultCode.InvalidParameter, null);
        if (string.IsNullOrWhiteSpace(data.SessionId)) return (ResultCode.InvalidParameter, null);

        var result = SessionManager.I.TryGetUser(data, out var user);
        return (result, user);
    }

    [HttpPost]
    [Route("gamestart")]
    [SwaggerOperation("게임 시작", "(WIP)")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status408RequestTimeout, "해당 API를 호출할 수 있는 시간이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다. loading API가 먼저 수행되어야 합니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(GameStartResponse))]
    public async Task<IActionResult> GameStart([FromBody] AuthBody req)
    {
        try
        {
            var (resultCode, user) = await ValidateToken(req.Token);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;
            
                case ResultCode.InvalidParameter:
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            var now = DateTime.UtcNow;
            if (now < user!.PlaySession!.GameStartAt) return this.StatusCode(StatusCodes.Status408RequestTimeout);
        
            return this.Json(new GameStartResponse
            {
                PlayerNicknames = user!.PlaySession!.GetNicknames().ToArray(),
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            this.logger.LogCritical(e, "API HAS EXCEPTION - gamestart [token : {token}]", req.Token);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("gameend")]
    [SwaggerOperation("게임 종료", "(WIP)")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status408RequestTimeout, "해당 API를 호출할 수 있는 시간이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(GameEndResponse))]
    public async Task<IActionResult> GameEnd([FromBody] PlayInfoBody req)
    {
        try
        {
            var (resultCode, user) = await ValidateToken(req.Token);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;
            
                case ResultCode.InvalidParameter:
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            var now = DateTime.UtcNow;
            if (now < user!.PlaySession!.GameEndAt) return this.StatusCode(StatusCodes.Status408RequestTimeout);
            
            resultCode = SessionManager.I.TryLeave(user, isQuit: false);

            if (resultCode != ResultCode.Ok || user.HasQuit)
                throw new InvalidOperationException(resultCode.ToString());
        
            return this.Json(new GameEndResponse()
            {
                GameReward = new GameReward
                {
                    RewardId = "TEST",
                    RewardImage = "https://placehold.jp/150x150.png"
                },
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            this.logger.LogCritical(e,
                "API HAS EXCEPTION - gameend [token : {token}, hostTime : {hostTime}, itemCount : {itemCount}]",
                req.Token, req.HostTime, req.ItemCount);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("gamequit")]
    [SwaggerOperation("게임 포기", "테스트를 위해 토큰 대신 userId를 입력해 게임을 포기시킵니다. (WIP)")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    public async Task<IActionResult> GameQuit([FromBody] AuthBody req)
    {
        try
        {
            var (resultCode, user) = await ValidateToken(req.Token);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;
            
                case ResultCode.InvalidParameter:
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            resultCode = SessionManager.I.TryLeave(user!, isQuit: true);

            if (resultCode != ResultCode.Ok || user?.HasQuit != true)
                throw new InvalidOperationException(resultCode.ToString());

            return this.Ok();
        }
        catch (Exception e)
        {
            this.logger.LogCritical(e,
                "API HAS EXCEPTION - gamequit [token : {token}]", req.Token);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("ranking")]
    [SwaggerOperation("게임 랭킹 정보 갱신", "(WIP)")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(RankingResponse))]
    public async Task<IActionResult> Ranking([FromBody] PlayInfoBody req)
    {
        if (string.IsNullOrWhiteSpace(req.Token)) return this.Unauthorized();
        
        return this.Json(new RankingResponse
        {
            MyRank = new RankInfo
            {
                UserId = "asd",
                UserNickname = "nickname1",
                Ranking = 1,
                HostTime = 123,
            },
            TopRank = new RankInfo
            {
                UserId = "asd",
                UserNickname = "nickname1",
                Ranking = 1,
                HostTime = 123,
            },
            CurrentTick = DateTime.UtcNow.Ticks,
        });
    }
    
    [HttpPost]
    [Route("result")]
    [SwaggerOperation("게임 최종 랭킹 결과 조회", "(WIP)")]
    [SwaggerResponse(StatusCodes.Status102Processing, "아직 계산이 완료되지 않았습니다. 다시 시도해주세요.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(RankingResponse))]
    public async Task<IActionResult> Result([FromBody] AuthBody req)
    {
        if (string.IsNullOrWhiteSpace(req.Token)) return this.Unauthorized();
        
        return this.Json(new RankingResponse
        {
            MyRank = new RankInfo
            {
                UserId = "asd",
                UserNickname = "nickname1",
                Ranking = 1,
                HostTime = 123,
            },
            TopRank = new RankInfo
            {
                UserId = "asd",
                UserNickname = "nickname1",
                Ranking = 1,
                HostTime = 123,
            },
            CurrentTick = DateTime.UtcNow.Ticks,
        });
    }
}