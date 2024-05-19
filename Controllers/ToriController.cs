using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;
using Swashbuckle.AspNetCore.Annotations;
using tori.AppApi;
using tori.AppApi.Model;
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
    private readonly ApiClient apiClient;

    public ToriController(ILogger<ToriController> logger, AppDbContext dbContext, ApiClient apiClient)
    {
        this.logger = logger;
        this.dbContext = dbContext;
        this.apiClient = apiClient;
    }
    
    // https://bold-meadow-582767.postman.co/workspace/meow~e50fbf18-f4b7-4c0d-a1b2-e0e2d151e54d/request/23935028-f9bcafb7-b36c-4f44-8ac2-4f5244a6bca4
    // - [x] 게임 정보 기록할 DB 테이블 구성
    // - [x] 게임 방 정보 앱 서버로부터 가져오기
    // - [x] 로딩 API에 추가한 값 앱 서버로부터 가져오기
    // - [x] 에너지 및 아이템 차감 처리하기
    // - [x] 1분 간 게임 기록 API 호출이 없는 유저 이탈 처리하기
    // - [x] 게임 결과 앱 서버로 보내기

    private static async Task<(ResultCode, SessionUser?)> ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return (ResultCode.InvalidParameter, null);
        
        var (isValid, data) = await JwtToken.Parse(token);
        if (!isValid || string.IsNullOrWhiteSpace(data.User.Id)) return (ResultCode.InvalidParameter, null);

        var result = SessionManager.I.TryGetUser(data, out var user);
        if (user.HasQuit) return (ResultCode.NotJoinedUser, user);
        
        return (result, user);
    }
    
    private async Task<IActionResult> HandleExceptionAsync(IDbContextTransaction? transaction, Exception e, string message,
        params object[] args)
    {
        if (transaction != null) await transaction.RollbackAsync();
        
#pragma warning disable CA2254
        this.logger.LogCritical(e, message, args);
#pragma warning restore CA2254

        var detail = e switch
        {
            MySqlException => "SQL Exception",
            InvalidOperationException exception => exception.Message,
            _ => null
        };

        return this.Problem(detail ?? "Failed to process operation", statusCode: StatusCodes.Status500InternalServerError);
    }

    private async Task SendUserStatusAsync(SessionUser sessionUser, Dictionary<int, int> spentItems, DateTime timestamp)
    {
        var playTime = timestamp - sessionUser.PlaySession!.GameStartAt;
        var spentEnergy = (int)Math.Floor(playTime.TotalMinutes) * Constants.EnergyCostPerMinutes;
        var curStatus = new UserStatus(
            sessionUser.UserId,
            spentItems,
            spentEnergy,
            timestamp);
        var delta = curStatus;

        if (sessionUser.CachedStatus != null)
        {
            delta = sessionUser.CachedStatus.Delta(curStatus);
        }

        sessionUser.CachedStatus = curStatus;

        await this.apiClient.PostAsync(API_URL.UserStatus, new StringContent(
            JsonSerializer.Serialize(delta),
            Encoding.UTF8,
            "application/json"));
    }

    [HttpPost]
    [Route("loading")]
    [SwaggerOperation("로딩", "게임 진입에 앞서 필요한 정보를 로딩합니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "이미 해당 유저가 게임에 참여 중이기 때문에 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(LoadingResponse))]
    public async Task<IActionResult> Loading([FromBody] LoadingBody req)
    {
        if (string.IsNullOrWhiteSpace(req.UserId)) return this.BadRequest();

        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var roomInfo = await this.apiClient.GetAsync<RoomInfo>(API_URL.RoomInfo, new Dictionary<string, string>
            {
                { "roomId", req.RoomId.ToString() },
            });

            if (roomInfo == null)
                throw new InvalidOperationException("Cannot found room info from APP API");

            var userInfo = await this.apiClient.GetAsync<UserInfo>(API_URL.UserInfo, new Dictionary<string, string>
            {
                { "userNo", req.UserId },
            });

            if (userInfo == null)
                throw new InvalidOperationException("Cannot found user info from APP API");
            
            var resultCode =
                SessionManager.I.TryJoin(new UserIdentifier(req.UserId, userInfo.Nickname), roomInfo, this.dbContext,
                    out var user, out var session);

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
            user.UserInfo = userInfo;

            var now = DateTime.UtcNow;
            var duplicate = await this.dbContext.GameUsers
                .Include(u => u.PlayData)
                .SingleOrDefaultAsync(u =>
                u.RoomId == session.RoomId
                && u.UserId == user.UserId);

            if (duplicate == null)
            {
                await this.dbContext.GameUsers.AddAsync(new GameUser
                {
                    RoomId = session.RoomId,
                    UserId = user.UserId,
                    Status = PlayStatus.Ready,
                    JoinedAt = user.JoinedAt,
                    LeavedAt = null,
                    PlayData = null,
                });
            }
            else if (duplicate.Status is PlayStatus.Disconnected or PlayStatus.Quit or PlayStatus.Done)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }
            else
            {
                if (duplicate.PlayData != null)
                {
                    this.dbContext.PlayData.Remove(duplicate.PlayData);
                }

                if (session.GameStartAt <= now)
                {
                    duplicate.Status = PlayStatus.Playing;
                }
                duplicate.JoinedAt = user.JoinedAt;
                duplicate.LeavedAt = null;
                duplicate.PlayData = null;
                this.dbContext.GameUsers.Update(duplicate);
            }
            await this.dbContext.SaveChangesAsync();

            var constants = await this.dbContext.GameConstants
                .ToDictionaryAsync(x => x.Key, x => x.Value);

            await transaction.CommitAsync();

            this.logger.LogDebug("loading [userId : {userId}, roomId : {roomId}, startAt : {startAt}, endAt : {endAt}]",
                user.UserId, session.RoomId, session.GameStartAt.ToLocalTime(), session.GameEndAt.ToLocalTime());
            
            return this.Json(new LoadingResponse
            {
                Token = JwtToken.ToToken(req.UserId, userInfo.Nickname, session.RoomId),
                Constants = constants,
                RoomId = session.RoomId,
                StageId = session.StageId,
                UserNickname = userInfo.Nickname,
                Energy = userInfo.Energy,
                WinnerCount = userInfo.WinCount,
                Items = userInfo.Inventory.ToDictionary(i => i.ItemNo.ToString(), i => i.ItemCount),
                GameReward = roomInfo.GoodsInfo.ToReward(),
                GameStartUtc = session.GameStartAt.Ticks,
                GameEndUtc = session.GameEndAt.Ticks,
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - loading [userId : {userId}, roomId : {roomId}]",
                req.UserId, req.RoomId);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
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

            var gameUser = await this.dbContext.GameUsers.SingleOrDefaultAsync(u =>
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

            user.LastActiveAt = now;
            gameUser.Status = PlayStatus.Playing;
            this.dbContext.GameUsers.Update(gameUser);
            await this.dbContext.SaveChangesAsync();

            await transaction.CommitAsync();
            return this.Json(new GameStartResponse
            {
                PlayerNicknames = user.PlaySession!.GetNicknames().ToArray(),
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e, "API HAS EXCEPTION - gamestart [token : {token}]",
                req.Token);
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

            var gameUser = await this.dbContext.GameUsers.SingleOrDefaultAsync(u =>
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
            gameUser.LeavedAt = now;
            this.dbContext.GameUsers.Update(gameUser);
            await this.dbContext.SaveChangesAsync();

            await this.SendUserStatusAsync(user, req.SpentItems, now);
            await transaction.CommitAsync();
            
            return this.Json(new GameEndResponse
            {
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - gameend [token : {token}, hostTime : {hostTime}, itemCount : {itemCount}]",
                req.Token, req.HostTime, req.ItemCount);
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

            var gameUser = await this.dbContext.GameUsers
                .Include(u => u.PlayData)
                .OrderBy(u => u.Id)
                .SingleOrDefaultAsync(u =>
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

            var now = DateTime.UtcNow;
            gameUser.Status = PlayStatus.Quit;
            gameUser.LeavedAt = now;
            this.dbContext.GameUsers.Update(gameUser);
            await this.dbContext.SaveChangesAsync();

            await this.SendUserStatusAsync(user, gameUser.PlayData!.SpentItems, now);
            await transaction.CommitAsync();

            return this.Ok();
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - gamequit [token : {token}]", req.Token);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    [HttpPost]
    [Route("play-data")]
    [SwaggerOperation("게임 기록", "게임 플레이 중 소모한 에너지와 아이템 정보를 기록합니다.")]
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

            var gameUser = await this.dbContext.GameUsers
                .Include(u => u.PlayData)
                .SingleOrDefaultAsync(u =>
                    u.RoomId == user.PlaySession.RoomId
                    && u.UserId == user.UserId);
            
            if (gameUser == null) throw new InvalidOperationException("Cannot found game user from DB");
            if (gameUser.Status != PlayStatus.Playing)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            var now = DateTime.UtcNow;
            user.LastActiveAt = now;

            var useItemsJson = JsonSerializer.Serialize(req.UsedItems
                .ToDictionary(it => int.Parse(it.Key), it => it.Value));
            if (gameUser.PlayData == null)
            {
                await this.dbContext.PlayData.AddAsync(new GamePlayData
                {
                    RoomId = user.PlaySession.RoomId,
                    UserId = user.UserId,
                    UseItems = useItemsJson,
                    TimeStamp = now,
                    GameUser = gameUser,
                });
            }
            else
            {
                var playData = gameUser.PlayData;
                playData.UseItems = useItemsJson;
                playData.TimeStamp = now;
                playData.GameUser = gameUser;
                this.dbContext.PlayData.Update(playData);
            }
            
            await this.dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            return this.Ok();
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e, "API HAS EXCEPTION - play-data [token : {token}]",
                req.Token);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    [HttpPost]
    [Route("status")]
    [SwaggerOperation("유저 정보 갱신", "play-data API를 통해 기록된 시간 정보와 아이템 사용 정보를 바탕으로 앱 서버에 에너지 및 아이템 차감을 요청합니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status([FromBody] StatusBody req)
    {
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            if (!int.TryParse(req.UserId, out var userId))
            {
                await transaction.RollbackAsync();
                return this.BadRequest();
            }

            var gameUser = await this.dbContext.GameUsers
                .Include(u => u.PlayData)
                .SingleOrDefaultAsync(u =>
                    u.UserId == userId
                    && u.Status == PlayStatus.Playing);

            if (gameUser == null)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            var user = SessionManager.I.TryGetUser(gameUser.UserId, gameUser.RoomId);
            if (user == null)
            {
                await transaction.RollbackAsync();
                return this.Conflict();
            }

            await this.SendUserStatusAsync(user, gameUser.PlayData!.SpentItems, gameUser.PlayData!.TimeStamp);
            await transaction.CommitAsync();
            
            return this.Ok();
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e, "API HAS EXCEPTION - status [userId : {userId}]",
                req.UserId);
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
                    RoomId = user.PlaySession.RoomId,
                    Ranking = mine.Ranking,
                    HostTime = mine.HostTime,
                },
                TopRank = new RankInfo
                {
                    UserId = first.Identifier.Id,
                    UserNickname = first.Identifier.Nickname,
                    RoomId = user.PlaySession.RoomId,
                    Ranking = first.Ranking,
                    HostTime = first.HostTime
                },
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(null, e,
                "API HAS EXCEPTION - ranking [token : {token}, hostTime : {hostTime}, itemCount : {itemCount}]",
                req.Token, req.HostTime, req.ItemCount);
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
                    RoomId = user.PlaySession.RoomId,
                    Ranking = mine.Ranking,
                    HostTime = mine.HostTime,
                },
                TopRank = new RankInfo
                {
                    UserId = first.Identifier.Id,
                    UserNickname = first.Identifier.Nickname,
                    RoomId = user.PlaySession.RoomId,
                    Ranking = first.Ranking,
                    HostTime = first.HostTime
                },
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(null, e, "API HAS EXCEPTION - result [token : {token}", req.Token);
        }
    }
}