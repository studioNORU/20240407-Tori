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
    private readonly AppDbContext dbContext;

    public ToriController(ILogger<ToriController> logger, AppDbContext dbContext)
    {
        this.logger = logger;
        this.dbContext = dbContext;
    }

    [HttpPost]
    [Route("loading")]
    [SwaggerOperation("로딩 (WIP)", "게임 진입에 앞서 필요한 정보를 로딩합니다. (WIP)")]
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

            var constants = this.dbContext.GameConstants.ToDictionary(x => x.Key, x => x.Value);
            
            return this.Json(new LoadingResponse
            {
                Token = JwtToken.ToToken(req.UserId, req.UserNickname, session.SessionId),
                Constants = constants,
                RoomId = (int)session.SessionId,
                StageId = session.StageId,
                //TODO: 실제 데이터를 사용해야 함
                GameReward = new GameReward
                {
                    RewardId = "TEST",
                    RewardImage = "https://placehold.jp/150x150.png"
                },
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

        var result = SessionManager.I.TryGetUser(data, out var user);
        if (user.HasQuit) return (ResultCode.NotJoinedUser, user);
        
        return (result, user);
    }

    [HttpPost]
    [Route("gamestart")]
    [SwaggerOperation("게임 시작", "대기를 종료하고 게임을 시작했다는 것을 알립니다.")]
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
                PlayerNicknames = user.PlaySession!.GetNicknames().ToArray(),
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
    [SwaggerOperation("게임 종료", "게임 플레이를 정상적으로 완료했음을 알립니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status408RequestTimeout, "해당 API를 호출할 수 있는 시간이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(GameEndResponse))]
    public async Task<IActionResult> GameEnd([FromBody] PlayInfoBody req)
    {
        try
        {
            if (req.HostTime < 0) return this.BadRequest();
            if (req.ItemCount < 0) return this.BadRequest();

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
            
            
            if (user.PlaySession == null) return this.Conflict();

            // 게임 플레이 시간보다 쿠폰 소지 시간이 더 길 수는 없습니다
            if ((now - user.PlaySession.GameStartAt).TotalSeconds < req.HostTime)
                return this.BadRequest();
            
            _ = user.PlaySession.UpdateRanking(user.Identifier, req.HostTime, req.ItemCount);
            
            resultCode = SessionManager.I.TryLeave(user, isQuit: false);

            if (resultCode != ResultCode.Ok || user.HasQuit)
                throw new InvalidOperationException(resultCode.ToString());
        
            return this.Json(new GameEndResponse()
            {
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
    [SwaggerOperation("게임 포기", "게임 방에서 유저를 이탈시킵니다.")]
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
    [SwaggerOperation("게임 랭킹 정보 갱신", "게임 랭킹 정보를 갱신하고 갱신된 랭킹 정보를 반환합니다.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(RankingResponse))]
    public async Task<IActionResult> Ranking([FromBody] PlayInfoBody req)
    {
        try
        {
            if (req.HostTime < 0) return this.BadRequest();
            if (req.ItemCount < 0) return this.BadRequest();
            
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

            if (user?.PlaySession == null) return this.Conflict();

            // 게임 플레이 시간보다 쿠폰 소지 시간이 더 길 수는 없습니다
            var now = DateTime.UtcNow;
            if ((now - user.PlaySession.GameStartAt).TotalSeconds < req.HostTime)
                return this.BadRequest();
            
            var (first, mine) = user.PlaySession.UpdateRanking(user.Identifier, req.HostTime, req.ItemCount);

            return this.Json(new RankingResponse
            {
                MyRank = new RankInfo
                {
                    UserId = mine.Identifier.Id,
                    UserNickname = mine.Identifier.Nickname,
                    RoomId = (int)user.PlaySession.SessionId,
                    Ranking = mine.Ranking,
                    HostTime = mine.HostTime,
                },
                TopRank = new RankInfo
                {
                    UserId = first.Identifier.Id,
                    UserNickname = first.Identifier.Nickname,
                    RoomId = (int)user.PlaySession.SessionId,
                    Ranking = first.Ranking,
                    HostTime = first.HostTime
                },
                CurrentTick = DateTime.UtcNow.Ticks,
            });
        }
        catch (Exception e)
        {
            this.logger.LogCritical(e,
                "API HAS EXCEPTION - ranking [token : {token}, hostTime : {hostTime}, itemCount : {itemCount}]",
                req.Token, req.HostTime, req.ItemCount);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("result")]
    [SwaggerOperation("게임 최종 랭킹 결과 조회 (WIP)", "(WIP)")]
    [SwaggerResponse(StatusCodes.Status102Processing, "아직 계산이 완료되지 않았습니다. 다시 시도해주세요.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(RankingResponse))]
    public async Task<IActionResult> Result([FromBody] AuthBody req)
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
                    return this.Conflict();
                case ResultCode.NotJoinedUser:
                    // 이미 gameend를 호출했으니 이 경우에는 정상
                    break;
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            if (user?.PlaySession == null) return this.Conflict();
            
            //TODO: 랭킹 집계가 완료되지 않았으면 Processing을 반환해야 함
            
            resultCode = user.PlaySession.TryGetRanking(user.Identifier, out var first, out var mine);
            if (resultCode != ResultCode.Ok) return this.Conflict();

            return this.Json(new RankingResponse
            {
                MyRank = new RankInfo
                {
                    UserId = mine.Identifier.Id,
                    UserNickname = mine.Identifier.Nickname,
                    RoomId = (int)user.PlaySession.SessionId,
                    Ranking = mine.Ranking,
                    HostTime = mine.ItemCount,
                },
                TopRank = new RankInfo
                {
                    UserId = first.Identifier.Id,
                    UserNickname = first.Identifier.Nickname,
                    RoomId = (int)user.PlaySession.SessionId,
                    Ranking = first.Ranking,
                    HostTime = first.ItemCount
                },
                CurrentTick = DateTime.UtcNow.Ticks,
            });
        }
        catch (Exception e)
        {
            this.logger.LogCritical(e,
                "API HAS EXCEPTION - result [token : {token}", req.Token);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}