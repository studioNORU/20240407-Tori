using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Tori.Controllers.Data;
using Tori.Controllers.Requests;
using tori.Controllers.Responses;
using tori.Core;
using tori.Models;
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
    
    // https://bold-meadow-582767.postman.co/workspace/meow~e50fbf18-f4b7-4c0d-a1b2-e0e2d151e54d/request/23935028-f9bcafb7-b36c-4f44-8ac2-4f5244a6bca4
    // - [x] 게임 정보 기록할 DB 테이블 구성
    // - [ ] 게임 방 정보 앱 서버로부터 가져오기
    // - [ ] 로딩 API에 추가한 값 앱 서버로부터 가져오기
    // - [ ] 에너지 및 아이템 차감 처리하기
    // - [ ] 1분 간 게임 기록 API 호출이 없는 유저 이탈 처리하기

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

        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var resultCode =
                SessionManager.I.TryJoin(new UserIdentifier(req.UserId, req.UserNickname), this.dbContext, out var user,
                    out var session);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;

                case ResultCode.AlreadyJoined:
                    await transaction.RollbackAsync();
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            if (user == null) throw new InvalidOperationException("User is null");
            var gameUser = await this.dbContext.GameUsers.AddAsync(new GameUser
            {
                RoomId = (int)session.RoomId,
                UserId = user.UserId,
                Status = PlayStatus.Ready,
                JoinedAt = user.JoinedAt,
                LeavedAt = null,
                PlayData = null,
            });

            var constants = await this.dbContext.GameConstants
                .ToDictionaryAsync(x => x.Key, x => x.Value);

            await transaction.CommitAsync();

            return this.Json(new LoadingResponse
            {
                Token = JwtToken.ToToken(req.UserId, req.UserNickname, session.RoomId),
                Constants = constants,
                RoomId = gameUser.Entity.RoomId,
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
            await transaction.RollbackAsync();
            this.logger.LogCritical(e, "API HAS EXCEPTION - loading [userId : {userId}, userNickname : {userNickname}]",
                req.UserId, req.UserNickname);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            await transaction.DisposeAsync();
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
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다. loading API가 먼저 수행되어야 합니다. 혹은 이미 gamestart가 처리되었습니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(GameStartResponse))]
    public async Task<IActionResult> GameStart([FromBody] AuthBody req)
    {
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var (resultCode, user) = await ValidateToken(req.Token);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;

                case ResultCode.InvalidParameter:
                    await transaction.RollbackAsync();
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    await transaction.RollbackAsync();
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            var now = DateTime.UtcNow;
            if (now < user.PlaySession.GameStartAt)
            {
                await transaction.RollbackAsync();
                return this.StatusCode(StatusCodes.Status408RequestTimeout);
            }

            var gameUser = await this.dbContext.GameUsers.FirstOrDefaultAsync(u =>
                u.RoomId == user.PlaySession.RoomId
                && u.UserId == user.UserId);
            
            if (gameUser == null) throw new InvalidOperationException("Cannot found game user from DB");
            if (gameUser.Status != PlayStatus.Ready)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            resultCode = SessionManager.I.Start(user);

            if (resultCode != ResultCode.Ok)
            {
                if (resultCode is ResultCode.SessionNotFound or ResultCode.NotJoinedUser or ResultCode.AlreadyJoined)
                {
                    await transaction.RollbackAsync();
                    return this.Conflict();
                }
                
                throw new InvalidOperationException(resultCode.ToString());
            }

            gameUser.Status = PlayStatus.Playing;
            this.dbContext.GameUsers.Update(gameUser);

            await transaction.CommitAsync();
            return this.Json(new GameStartResponse
            {
                PlayerNicknames = user.PlaySession!.GetNicknames().ToArray(),
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            this.logger.LogCritical(e, "API HAS EXCEPTION - gamestart [token : {token}]", req.Token);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }
    
    [HttpPost]
    [Route("gameend")]
    [SwaggerOperation("게임 종료", "게임 플레이를 종료합니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(GameEndResponse))]
    public async Task<IActionResult> GameEnd([FromBody] PlayInfoBody req)
    {
        if (req.HostTime < 0) return this.BadRequest();
        if (req.ItemCount < 0) return this.BadRequest();

        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var (resultCode, user) = await ValidateToken(req.Token);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;

                case ResultCode.InvalidParameter:
                    await transaction.RollbackAsync();
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    await transaction.RollbackAsync();
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            var gameUser = await this.dbContext.GameUsers.FirstOrDefaultAsync(u =>
                u.RoomId == user.PlaySession.RoomId
                && u.UserId == user.UserId);
            
            if (gameUser == null) throw new InvalidOperationException("Cannot found game user from DB");
            if (gameUser.Status != PlayStatus.Playing)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            // 게임 플레이 시간보다 쿠폰 소지 시간이 더 길 수는 없습니다
            var now = DateTime.UtcNow;
            if ((now - user.PlaySession.GameStartAt).TotalSeconds < req.HostTime)
            {
                await transaction.RollbackAsync();
                return this.BadRequest();
            }

            _ = user.PlaySession.UpdateRanking(user.Identifier, req.HostTime, req.ItemCount);

            resultCode = SessionManager.I.TryLeave(user, isQuit: false);

            if (resultCode != ResultCode.Ok)
            {
                if (resultCode is ResultCode.SessionNotFound or ResultCode.NotJoinedUser)
                {
                    await transaction.RollbackAsync();
                    return this.Conflict();
                }
                throw new InvalidOperationException(resultCode.ToString());
            }

            // 최종 집계를 진행할 수 있는 시간이라면 집계 마감 관련 처리를 합니다
            if (user.PlaySession.GameEndAt <= now)
                user.PlaySession.ReserveClose();

            gameUser.Status = PlayStatus.Done;
            gameUser.LeavedAt = DateTime.UtcNow;
            this.dbContext.GameUsers.Update(gameUser);
            await transaction.CommitAsync();
            
            return this.Json(new GameEndResponse
            {
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            this.logger.LogCritical(e,
                "API HAS EXCEPTION - gameend [token : {token}, hostTime : {hostTime}, itemCount : {itemCount}]",
                req.Token, req.HostTime, req.ItemCount);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }
    
    [HttpPost]
    [Route("gamequit")]
    [SwaggerOperation("게임 포기", "게임 방에서 유저를 이탈시킵니다.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    public async Task<IActionResult> GameQuit([FromBody] PlayInfoBody req)
    {
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var (resultCode, user) = await ValidateToken(req.Token);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;

                case ResultCode.InvalidParameter:
                    await transaction.RollbackAsync();
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    await transaction.RollbackAsync();
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            var gameUser = await this.dbContext.GameUsers.FirstOrDefaultAsync(u =>
                u.RoomId == user.PlaySession.RoomId
                && u.UserId == user.UserId);
            
            if (gameUser == null) throw new InvalidOperationException("Cannot found game user from DB");
            if (gameUser.Status != PlayStatus.Playing)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            resultCode = SessionManager.I.TryLeave(user, isQuit: true);

            if (resultCode != ResultCode.Ok || user.HasQuit != true)
            {
                if (resultCode is ResultCode.SessionNotFound or ResultCode.NotJoinedUser)
                {
                    await transaction.RollbackAsync();
                    return this.Conflict();
                }
                throw new InvalidOperationException(resultCode.ToString());
            }

            gameUser.Status = PlayStatus.Quit;
            gameUser.LeavedAt = DateTime.UtcNow;
            this.dbContext.GameUsers.Update(gameUser);
            await transaction.CommitAsync();

            return this.Ok();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            this.logger.LogCritical(e,
                "API HAS EXCEPTION - gamequit [token : {token}]", req.Token);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    [HttpPost]
    [Route("play-data")]
    [SwaggerOperation("게임 기록 (WIP)", "(WIP)")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    public async Task<IActionResult> PlayData([FromBody] PlayDataBody req)
    {
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var (resultCode, user) = await ValidateToken(req.Token);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;

                case ResultCode.InvalidParameter:
                    await transaction.RollbackAsync();
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    await transaction.RollbackAsync();
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            var gameUser = await this.dbContext.GameUsers.FirstOrDefaultAsync(u =>
                u.RoomId == user.PlaySession.RoomId
                && u.UserId == user.UserId);
            
            if (gameUser == null) throw new InvalidOperationException("Cannot found game user from DB");
            if (gameUser.Status != PlayStatus.Playing)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            await this.dbContext.PlayData.AddAsync(new GamePlayData
            {
                RoomId = (int)user.PlaySession.RoomId,
                UserId = user.UserId,
                UseItems = JsonSerializer.Serialize(req.UsedItems),
                TimeStamp = DateTime.UtcNow,
                GameUser = gameUser,
            });
            
            await transaction.CommitAsync();

            return this.Ok();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            this.logger.LogCritical(e, "API HAS EXCEPTION - play-data [token : {token}]", req.Token);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
        finally
        {
            await transaction.DisposeAsync();
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

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft) return this.Conflict();

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
                    RoomId = (int)user.PlaySession.RoomId,
                    Ranking = mine.Ranking,
                    HostTime = mine.HostTime,
                },
                TopRank = new RankInfo
                {
                    UserId = first.Identifier.Id,
                    UserNickname = first.Identifier.Nickname,
                    RoomId = (int)user.PlaySession.RoomId,
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
    [SwaggerOperation("게임 최종 랭킹 결과 조회", "집계가 완료된 최종 랭킹 정보를 조회합니다.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status408RequestTimeout, "아직 계산이 완료되지 않았습니다. 다시 시도해주세요.")]
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

            if (user?.PlaySession == null || !user.HasJoined || user.HasQuit) return this.Conflict();
            
            var now = DateTime.UtcNow;
            if (user.PlaySession.CloseAt == null || now < user.PlaySession.CloseAt)
                return this.StatusCode(StatusCodes.Status408RequestTimeout);
            
            resultCode = user.PlaySession.TryGetRanking(user.Identifier, out var first, out var mine);
            if (resultCode != ResultCode.Ok) return this.Conflict();

            return this.Json(new RankingResponse
            {
                MyRank = new RankInfo
                {
                    UserId = mine.Identifier.Id,
                    UserNickname = mine.Identifier.Nickname,
                    RoomId = (int)user.PlaySession.RoomId,
                    Ranking = mine.Ranking,
                    HostTime = mine.HostTime,
                },
                TopRank = new RankInfo
                {
                    UserId = first.Identifier.Id,
                    UserNickname = first.Identifier.Nickname,
                    RoomId = (int)user.PlaySession.RoomId,
                    Ranking = first.Ranking,
                    HostTime = first.HostTime
                },
                CurrentTick = DateTime.UtcNow.Ticks,
            });
        }
        catch (Exception e)
        {
            this.logger.LogCritical(e, "API HAS EXCEPTION - result [token : {token}", req.Token);
            return this.Problem("Failed to process operation.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}