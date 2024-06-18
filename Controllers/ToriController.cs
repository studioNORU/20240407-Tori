using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;
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
    private readonly DataFetcher dataFetcher;

    public ToriController(ILogger<ToriController> logger, AppDbContext dbContext, DataFetcher dataFetcher)
    {
        this.logger = logger;
        this.dbContext = dbContext;
        this.dataFetcher = dataFetcher;
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

    private async Task WriteLog(LogType logType, string? userId, int? roomId, string clientVersion, string message, object? dataObject = null)
    {
        this.dbContext.Logs.Add(new GameLog
        {
            LogType = logType.ToString(),
            UserId = userId,
            RoomId = roomId,
            ClientVersion = clientVersion,
            Message = message,
            DataJson = dataObject == null ? null : JsonSerializer.Serialize(dataObject),
            CreatedAtKST = DateTime.UtcNow.AddHours(9),
        });

        await this.dbContext.SaveChangesAsync();
    }
    
    [HttpPost]
    [Route("dev-check")]
    [SwaggerOperation("[테스트용] 게임 접속 가능 여부 조회", "게임 접속이 가능한지와 관련 정보를 조회합니다.")]
    public async Task<IActionResult> Check([FromBody] LoadingBody req)
    {
        this.logger.LogInformation("[POST] /tori/dev-check - [body : {json}]", JsonSerializer.Serialize(req));

        var now = DateTime.UtcNow;
        var res = new DevCheckResponse
        {
            CurrentTick = now.Ticks,
            RequestedKst = ToKstString(now),
        };

        if (string.IsNullOrWhiteSpace(req.UserId) || !int.TryParse(req.UserId, out _))
        {
            res.ResultCode = ResultCode.InvalidParameter;
            res.Message = "UserId는 정수형을 사용해야합니다.";

            await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
            return this.Ok(res);
        }

        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var roomInfo = await this.dataFetcher.GetRoomInfo(req.RoomId);

            if (roomInfo == null)
            {
                res.ResultCode = ResultCode.DataNotFound;
                res.Message = "게임 방 정보를 찾지 못했습니다.";

                await transaction.RollbackAsync();
                await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
                return this.Ok(res);
            }

            res.RoomId = roomInfo.RoomId;
            res.GameReward = roomInfo.GoodsInfo.ToReward(roomInfo.ToEnum());
            res.GameStartKst = ToKstString(roomInfo.BeginRunningTime);
            res.GameEndKst = ToKstString(roomInfo.EndRunningTime);

            var userInfo = await this.dataFetcher.GetUserInfo(req.UserId);

            if (userInfo == null)
            {
                res.ResultCode = ResultCode.DataNotFound;
                res.Message = "유저 정보를 찾지 못했습니다.";
                
                await transaction.RollbackAsync();
                await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
                return this.Ok(res);
            }

#if DEBUG || DEV
            if (roomInfo is TestRoomInfo && userInfo is not TestUserInfo)
            {
                res.ResultCode = ResultCode.CannotUseBothNormalTest;
                res.Message = "일반 유저는 디버깅을 위한 테스트 방에 접근할 수 없습니다.";
                
                await transaction.RollbackAsync();
                await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
                return this.Ok(res);
            }

            if (roomInfo is not TestRoomInfo && userInfo is TestUserInfo)
            {
                res.ResultCode = ResultCode.CannotUseBothNormalTest;
                res.Message = "디버깅을 위한 테스트 유저는 일반 게임 방에 접근할 수 없습니다.";
                
                await transaction.RollbackAsync();
                await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
                return this.Ok(res);
            }
#endif

            if (now <= roomInfo.BeginRunningTime.AddMinutes(-Constants.PreloadDurationMinutes))
            {
                res.ResultCode = ResultCode.CannotJoinToRoomBeforePreload;
                res.Message = $"게임 시작 시간으로부터 ${Constants.PreloadDurationMinutes} 분 전부터 로딩 가능합니다. 그 이전에는 로딩을 할 수 없습니다.";
                
                await transaction.RollbackAsync();
                await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
                return this.Ok(res);
            }

            if (roomInfo.EndRunningTime <= now)
            {
                res.ResultCode = ResultCode.CannotJoinToEndedGame;
                res.Message = "게임이 종료된 방에는 참가할 수 없습니다.";
                
                await transaction.RollbackAsync();
                await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
                return this.Ok(res);
            }

            var userIdentifier = new UserIdentifier(req.UserId, userInfo.Nickname);
            if (roomInfo.BeginRunningTime <= now)
            {
                if (SessionManager.I.GetResumableSession(this.dbContext, userIdentifier) != null)
                {
                    res.ResultCode = ResultCode.CanResumeGame;
                    res.Message = "게임이 진행 중인 방이지만, 해당 유저는 이어서 플레이를 하기 위해 로딩을 요청할 수 있습니다.";
                }
                else
                {
                    res.ResultCode = ResultCode.CannotJoinToStartedGame;
                    res.Message = "게임이 진행 중인 방에 도중 참가는 허용되지 않습니다.";
                }

                await transaction.RollbackAsync();
                await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
                return this.Ok(res);
            }

            var session = SessionManager.I.GetSession(req.RoomId);
            if (session != null)
            {
                if (session.TryGetUser(userIdentifier, out _) == ResultCode.Ok)
                {
                    res.ResultCode = ResultCode.AlreadyJoined;
                    res.Message = "이미 해당 방에 접속해 있는 유저입니다.";
                    
                    await transaction.RollbackAsync();
                    await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
                    return this.Ok(res);
                }

                if (roomInfo.PlayerCount <= session.GetUseCount())
                {
                    res.ResultCode = ResultCode.CannotJoinToFullRoom;
                    res.Message = "인원이 꽉 찬 방에는 접근할 수 없습니다.";
                    
                    await transaction.RollbackAsync();
                    await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
                    return this.Ok(res);
                }
            }

            res.ResultCode = ResultCode.Ok;
            res.Message = "해당 게임 방에 참가할 수 있는 유저입니다.";
            
            await transaction.CommitAsync();
            await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, res.Message, res);
            return this.Ok(res);
        }
        catch (Exception e)
        {
            res.ResultCode = ResultCode.UnhandledError;
            res.Message = e.Message;
            
            await transaction.RollbackAsync();
            await this.WriteLog(LogType.CheckData, req.UserId, req.RoomId, req.ClientVersion, e.Message, res);
            return this.Ok(res);
        }
        finally
        {
            await transaction.DisposeAsync();
        }

        string ToKstString(DateTime dateTime)
        {
            var kst = dateTime.AddHours(9);
            return $"{kst.Year:0000}-{kst.Month:00}-{kst.Day:00} {kst.Hour:00}:{kst.Minute:00}:{kst.Second:00}.{kst.Millisecond}";
        }
    }

    [HttpPost]
    [Route("loading")]
    [SwaggerOperation("로딩", "게임 진입에 앞서 필요한 정보를 로딩합니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "이미 해당 유저가 게임에 참여 중이기 때문에 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status200OK, type: typeof(LoadingResponse))]
    public async Task<IActionResult> Loading([FromBody] LoadingBody req)
    {
        this.logger.LogInformation("[POST] /tori/loading - [body : {json}]", JsonSerializer.Serialize(req));
        if (string.IsNullOrWhiteSpace(req.UserId))
        {
            await this.WriteLog(LogType.Loading, req.UserId, req.RoomId, req.ClientVersion, "UserId가 입력되지 않았습니다", req);
            return this.BadRequest();
        }

        var message = string.Empty;
        object? dataObject = req;
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var roomInfo = await this.dataFetcher.GetRoomInfo(req.RoomId);

            if (roomInfo == null)
                throw new InvalidOperationException("Cannot found room info from APP API");

            var userInfo = await this.dataFetcher.GetUserInfo(req.UserId);

            if (userInfo == null)
                throw new InvalidOperationException("Cannot found user info from APP API");
            
#if DEBUG || DEV
            if (roomInfo is TestRoomInfo && userInfo is not TestUserInfo)
                throw new InvalidOperationException("Cannot join normal user join to test room");
            
            if (roomInfo is not TestRoomInfo && userInfo is TestUserInfo)
                throw new InvalidOperationException("Cannot join test user join to normal room");
#endif
            
            var resultCode =
                SessionManager.I.TryJoin(new UserIdentifier(req.UserId, userInfo.Nickname), roomInfo, this.dbContext,
                    out var user, out var session);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;

                case ResultCode.AlreadyJoined:
                    await transaction.RollbackAsync();
                    message = "이미 해당 방에 접속해 플레이 중입니다.";
                    dataObject = new { session?.GameStartAt, session?.GameEndAt };
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    dataObject = new { session?.GameStartAt, session?.GameEndAt };
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

            var status = PlayStatus.Ready;
            if (session.GameStartAt <= now)
            {
                status = PlayStatus.Playing;
            }
            if (duplicate == null)
            {
                await this.dbContext.GameUsers.AddAsync(new GameUser
                {
                    RoomId = session.RoomId,
                    UserId = user.UserId,
                    Status = status,
                    JoinedAt = user.JoinedAt,
                    LeavedAt = null,
                    PlayData = null,
                });
            }
            else if (duplicate.Status is PlayStatus.Disconnected or PlayStatus.Quit)
            {
                await transaction.RollbackAsync();
                message = "진행을 포기한 게임에 다시 접속을 시도했습니다.";
                dataObject = new { Status = duplicate.Status.ToString() };
                return this.Conflict();
            }
            else
            {
                if (duplicate.PlayData != null)
                {
                    this.dbContext.PlayData.Remove(duplicate.PlayData);
                }

                duplicate.Status = status;
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

            message = "로딩에 성공했습니다.";
            dataObject = new
            {
                session.GameStartAt,
                session.GameEndAt,
            };
            
            return this.Json(new LoadingResponse
            {
                Token = JwtToken.ToToken(req.UserId, userInfo.Nickname, session.RoomId),
                Constants = constants,
                StageId = session.StageId,
                UserNickname = userInfo.Nickname,
                Energy = userInfo.Energy,
                WinnerCount = userInfo.WinCount,
                Items = userInfo.Inventory.ToDictionary(i => i.ItemNo.ToString(), i => i.ItemCount),
                GameReward = roomInfo.GoodsInfo.ToReward(roomInfo.ToEnum()),
                GameStartUtc = session.GameStartAt.Ticks,
                GameEndUtc = session.GameEndAt.Ticks,
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            message = e.Message;
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - loading [userId : {userId}, roomId : {roomId}]",
                req.UserId, req.RoomId);
        }
        finally
        {
            await transaction.DisposeAsync();
            await this.WriteLog(LogType.Loading, req.UserId, req.RoomId, req.ClientVersion, message, dataObject);
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
        this.logger.LogInformation("[POST] /tori/gamestart - [body : {json}]", JsonSerializer.Serialize(req));
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        string? userId = null;
        int? roomId = null;
        var message = string.Empty;
        object? dataObject = req;
        try
        {
            var (resultCode, user) = await ValidateToken(req.Token);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;

                case ResultCode.InvalidParameter:
                    await transaction.RollbackAsync();
                    message = "정상적인 토큰이 아닙니다.";
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    await transaction.RollbackAsync();
                    message = "해당 토큰을 통해 유저의 접속 정보를 확인하지 못했습니다.";
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            userId = user?.Identifier.Id;
            roomId = user?.PlaySession?.RoomId;

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft)
            {
                await transaction.RollbackAsync();
                message = "유저의 게임 참가 정보가 비정상적입니다.";
                dataObject = new
                {
                    user?.PlaySession?.RoomId,
                    user?.HasJoined,
                    user?.HasLeft,
                };
                return this.Conflict();
            }

            var now = DateTime.UtcNow;
            if (now < user.PlaySession.GameStartAt)
            {
                await transaction.RollbackAsync();
                message = "아직 게임 시작 시간이 되지 않았습니다.";
                dataObject = new
                {
                    user.PlaySession.GameStartAt,
                    user.PlaySession.GameEndAt,
                    now,
                };
                return this.StatusCode(StatusCodes.Status408RequestTimeout);
            }

            var gameUser = await this.dbContext.GameUsers.SingleOrDefaultAsync(u =>
                u.RoomId == user.PlaySession.RoomId
                && u.UserId == user.UserId);
            
            if (gameUser == null) throw new InvalidOperationException("Cannot found game user from DB");
            if (gameUser.Status != PlayStatus.Ready)
            {
                await transaction.RollbackAsync();
                message = "해당 유저가 준비 상태가 아닙니다.";
                dataObject = new
                {
                    Status = gameUser.Status.ToString(),
                };
                return this.Conflict();
            }

            resultCode = SessionManager.I.Start(user);

            if (resultCode != ResultCode.Ok)
            {
                if (resultCode is ResultCode.SessionNotFound or ResultCode.NotJoinedUser or ResultCode.AlreadyJoined)
                {
                    await transaction.RollbackAsync();
                    message = "해당 유저의 게임을 시작하지 못했습니다.";
                    dataObject = new
                    {
                        Resultcode = resultCode.ToString(),
                    };
                    return this.Conflict();
                }
                
                throw new InvalidOperationException(resultCode.ToString());
            }

            user.LastActiveAt = now;
            gameUser.Status = PlayStatus.Playing;
            this.dbContext.GameUsers.Update(gameUser);
            await this.dbContext.SaveChangesAsync();

            await transaction.CommitAsync();
            message = "게임을 시작했습니다.";
            return this.Json(new GameStartResponse
            {
                PlayerNicknames = user.PlaySession!.GetNicknames().ToArray(),
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            message = e.Message;
            return await this.HandleExceptionAsync(transaction, e, "API HAS EXCEPTION - gamestart [token : {token}]",
                req.Token);
        }
        finally
        {
            await transaction.DisposeAsync();
            await this.WriteLog(LogType.GameStart, userId, roomId, req.ClientVersion, message, dataObject);
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
        this.logger.LogInformation("[POST] /tori/gameend - [body : {json}]", JsonSerializer.Serialize(req));

        string? userId = null;
        int? roomId = null;
        var message = string.Empty;
        object? dataObject = req;

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
                    message = "정상적인 토큰이 아닙니다.";
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    await transaction.RollbackAsync();
                    message = "해당 토큰을 통해 유저의 접속 정보를 확인하지 못했습니다.";
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            userId = user?.Identifier.Id;
            roomId = user?.PlaySession?.RoomId;

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft)
            {
                await transaction.RollbackAsync();
                message = "유저의 게임 참가 정보가 비정상적입니다.";
                dataObject = new
                {
                    user?.PlaySession?.RoomId,
                    user?.HasJoined,
                    user?.HasLeft,
                };
                return this.Conflict();
            }
            
            if (req.HostTime < 0)
            {
                await transaction.RollbackAsync();
                message = "호스트 시간은 음수일 수 없습니다.";
                return this.BadRequest();
            }
            if (req.ItemCount < 0)
            {
                await transaction.RollbackAsync();
                message = "아이템 사용 횟수는 음수일 수 없습니다.";
                return this.BadRequest();
            }

            var gameUser = await this.dbContext.GameUsers.SingleOrDefaultAsync(u =>
                u.RoomId == user.PlaySession.RoomId
                && u.UserId == user.UserId);
            
            if (gameUser == null) throw new InvalidOperationException("Cannot found game user from DB");
            if (gameUser.Status != PlayStatus.Playing)
            {
                await transaction.RollbackAsync();
                message = "해당 유저가 플레이 상태가 아닙니다.";
                dataObject = new
                {
                    Status = gameUser.Status.ToString(),
                };
                return this.Conflict();
            }

            // 게임 플레이 시간보다 쿠폰 소지 시간이 더 길 수는 없습니다
            var now = DateTime.UtcNow;
            if ((now - user.PlaySession.GameStartAt).TotalSeconds < req.HostTime)
            {
                await transaction.RollbackAsync();
                message = "게임 플레이 시간보다 호스트 시간이 더 길 수 없습니다.";
                dataObject = new
                {
                    user.PlaySession.GameStartAt,
                    now,
                    req.HostTime,
                };
                return this.BadRequest();
            }

            _ = user.PlaySession.UpdateRanking(user.Identifier, req.HostTime, req.ItemCount);

            resultCode = SessionManager.I.TryLeave(user, isQuit: false);

            if (resultCode != ResultCode.Ok)
            {
                if (resultCode is ResultCode.SessionNotFound or ResultCode.NotJoinedUser)
                {
                    await transaction.RollbackAsync();
                    message = "게임 종료 처리에 실패했습니다.";
                    dataObject = new
                    {
                        ResultCode = resultCode.ToString(),
                    };
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

            await this.dataFetcher.UpdateUserStatus(user, req.SpentItems, now);
            await transaction.CommitAsync();

            message = "게임 종료에 성공했습니다.";
            dataObject = new
            {
                user.PlaySession.CloseAt
            };

            return this.Json(new GameEndResponse
            {
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            message = e.Message;
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - gameend [token : {token}, hostTime : {hostTime}, itemCount : {itemCount}]",
                req.Token, req.HostTime, req.ItemCount);
        }
        finally
        {
            await transaction.DisposeAsync();
            await this.WriteLog(LogType.GameEnd, userId, roomId, req.ClientVersion, message, dataObject);
        }
    }
    
    [HttpPost]
    [Route("gamequit")]
    [SwaggerOperation("게임 포기", "게임 방에서 유저를 이탈시킵니다.")]
    [SwaggerResponse(StatusCodes.Status401Unauthorized, "유효한 토큰이 아닙니다.")]
    [SwaggerResponse(StatusCodes.Status409Conflict, "게임에 참여하지 않은 유저입니다.")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    public async Task<IActionResult> GameQuit([FromBody] GameQuitBody req)
    {
        this.logger.LogInformation("[POST] /tori/gamequit - [body : {json}]", JsonSerializer.Serialize(req));

        string? userId = null;
        int? roomId = null;
        var message = string.Empty;
        object? dataObject = req;
        
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
                    message = "정상적인 토큰이 아닙니다.";
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    await transaction.RollbackAsync();
                    message = "해당 토큰을 통해 유저의 접속 정보를 확인하지 못했습니다.";
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            userId = user?.Identifier.Id;
            roomId = user?.PlaySession?.RoomId;

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft)
            {
                await transaction.RollbackAsync();
                message = "유저의 게임 참가 정보가 비정상적입니다.";
                dataObject = new
                {
                    user?.PlaySession?.RoomId,
                    user?.HasJoined,
                    user?.HasLeft,
                };
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
                message = "해당 유저가 플레이 상태가 아닙니다.";
                dataObject = new
                {
                    Status = gameUser.Status.ToString(),
                };
                return this.Conflict();
            }

            resultCode = SessionManager.I.TryLeave(user, isQuit: true);

            if (resultCode != ResultCode.Ok || user.HasQuit != true)
            {
                if (resultCode is ResultCode.SessionNotFound or ResultCode.NotJoinedUser)
                {
                    await transaction.RollbackAsync();
                    message = "해당 유저의 게임 포기를 처리하지 못했습니다.";
                    dataObject = new
                    {
                        ResultCode = resultCode.ToString(),
                    };
                    return this.Conflict();
                }
                throw new InvalidOperationException(resultCode.ToString());
            }

            var now = DateTime.UtcNow;
            gameUser.Status = PlayStatus.Quit;
            gameUser.LeavedAt = now;
            this.dbContext.GameUsers.Update(gameUser);
            await this.dbContext.SaveChangesAsync();
            
#if DEBUG || DEV
            if (user.UserInfo is TestUserInfo)
            {
                this.dbContext.GameUsers.Remove(gameUser);
                await this.dbContext.SaveChangesAsync();
            }
#endif

            await this.dataFetcher.UpdateUserStatus(user, req.SpentItems, now);
            await transaction.CommitAsync();

            message = "게임 포기에 성공했습니다.";

            return this.Ok();
        }
        catch (Exception e)
        {
            message = e.Message;
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - gamequit [token : {token}, itemCount : {itemCount}]", req.Token, req.ItemCount);
        }
        finally
        {
            await transaction.DisposeAsync();
            await this.WriteLog(LogType.GameQuit, userId, roomId, req.ClientVersion, message, dataObject);
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
        this.logger.LogInformation("[POST] /tori/play-data - [body : {json}]", JsonSerializer.Serialize(req));

        string? userId = null;
        int? roomId = null;
        var message = string.Empty;
        object? dataObject = req;
        
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
                    message = "정상적인 토큰이 아닙니다.";
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                case ResultCode.NotJoinedUser:
                    await transaction.RollbackAsync();
                    message = "해당 토큰을 통해 유저의 접속 정보를 확인하지 못했습니다.";
                    return this.Conflict();
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            userId = user?.Identifier.Id;
            roomId = user?.PlaySession?.RoomId;

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft)
            {
                await transaction.RollbackAsync();
                message = "유저의 게임 참가 정보가 비정상적입니다.";
                dataObject = new
                {
                    user?.PlaySession?.RoomId,
                    user?.HasJoined,
                    user?.HasLeft,
                };
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
                message = "해당 유저가 플레이 상태가 아닙니다.";
                dataObject = new
                {
                    Status = gameUser.Status.ToString(),
                };
                return this.Conflict();
            }

            var now = DateTime.UtcNow;
            user.LastActiveAt = now;

            var useItemsJson = JsonSerializer.Serialize(req.UsedItems
                .ToDictionary(it => int.Parse(it.Key), it => it.Value));
            if (gameUser.PlayData == null)
            {
                var playData = new GamePlayData
                {
                    RoomId = user.PlaySession.RoomId,
                    UserId = user.UserId,
                    UseItems = useItemsJson,
                    TimeStamp = now,
                    GameUser = gameUser,
                };
                dataObject = new
                {
                    playData.RoomId,
                    playData.UserId,
                    playData.UseItems,
                    playData.TimeStamp,
                };
                await this.dbContext.PlayData.AddAsync(playData);
            }
            else
            {
                var playData = gameUser.PlayData;
                playData.UseItems = useItemsJson;
                playData.TimeStamp = now;
                playData.GameUser = gameUser;
                this.dbContext.PlayData.Update(playData);
                dataObject = new
                {
                    playData.RoomId,
                    playData.UserId,
                    playData.UseItems,
                    playData.TimeStamp,
                };
            }
            
            await this.dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            message = "플레이 정보 갱신에 성공했습니다.";

            return this.Ok();
        }
        catch (Exception e)
        {
            message = e.Message;
            return await this.HandleExceptionAsync(transaction, e, "API HAS EXCEPTION - play-data [token : {token}]",
                req.Token);
        }
        finally
        {
            await transaction.DisposeAsync();
            await this.WriteLog(LogType.PlayData, userId, roomId, req.ClientVersion, message, dataObject);
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
        this.logger.LogInformation("[POST] /tori/status - [body : {json}]", JsonSerializer.Serialize(req));

        int? roomId = null;
        var message = string.Empty;
        object? dataObject = req;
        
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            if (!int.TryParse(req.UserId, out var userId))
            {
                await transaction.RollbackAsync();
                message = "유저 ID는 정수형 값을 사용해야 합니다.";
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
                message = "DB로부터 GameUser 정보를 조회하지 못했습니다.";
                dataObject = new
                {
                    userId,
                };
                return this.Conflict();
            }

            roomId = gameUser.RoomId;

            var user = SessionManager.I.TryGetUser(gameUser.UserId, gameUser.RoomId);
            if (user == null)
            {
                await transaction.RollbackAsync();
                message = "유저 정보를 찾지 못했습니다.";
                return this.Conflict();
            }

            await this.dataFetcher.UpdateUserStatus(user, gameUser.PlayData!.SpentItems, gameUser.PlayData!.TimeStamp);
            await transaction.CommitAsync();

            message = "유저 상태 갱신에 성공했습니다.";
            dataObject = new
            {
                gameUser.PlayData.SpentItems,
                gameUser.PlayData.TimeStamp,
            };
            
            return this.Ok();
        }
        catch (Exception e)
        {
            message = e.Message;
            return await this.HandleExceptionAsync(transaction, e, "API HAS EXCEPTION - status [userId : {userId}]",
                req.UserId);
        }
        finally
        {
            await transaction.DisposeAsync();
            await this.WriteLog(LogType.Status, req.UserId, roomId, req.ClientVersion, message, dataObject);
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
        this.logger.LogInformation("[POST] /tori/ranking - [body : {json}]", JsonSerializer.Serialize(req));

        string? userId = null;
        int? roomId = null;
        var message = string.Empty;
        object? dataObject = req;

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

            userId = user?.Identifier.Id;
            roomId = user?.PlaySession?.RoomId;

            if (user?.PlaySession == null || !user.HasJoined || user.HasLeft)
            {
                message = "유저의 게임 참가 정보가 비정상적입니다.";
                dataObject = new
                {
                    user?.PlaySession?.RoomId,
                    user?.HasJoined,
                    user?.HasLeft,
                };
                return this.Conflict();
            }

            if (req.HostTime < 0)
            {
                message = "호스트 시간은 음수일 수 없습니다.";
                return this.BadRequest();
            }

            if (req.ItemCount < 0)
            {
                message = "아이템 사용 횟수는 음수일 수 없습니다.";
                return this.BadRequest();
            }

            // 게임 플레이 시간보다 쿠폰 소지 시간이 더 길 수는 없습니다
            var now = DateTime.UtcNow;
            if ((now - user.PlaySession.GameStartAt).TotalSeconds < req.HostTime)
            {
                message = "게임 플레이 시간보다 호스트 시간이 더 길 수 없습니다.";
                dataObject = new
                {
                    user.PlaySession.GameStartAt,
                    now,
                    req.HostTime,
                };
                return this.BadRequest();
            }

            var (first, mine) = user.PlaySession.UpdateRanking(user.Identifier, req.HostTime, req.ItemCount);

            message = "랭킹 정보 업데이트에 성공했습니다.";
            dataObject = new
            {
                first,
                mine,
            };

            return this.Json(new RankingResponse
            {
                MyRank = new RankInfo
                {
                    UserId = mine.Identifier.Id,
                    UserNickname = mine.Identifier.Nickname,
                    Ranking = mine.Ranking,
                    HostTime = mine.HostTime,
                },
                TopRank = new RankInfo
                {
                    UserId = first.Identifier.Id,
                    UserNickname = first.Identifier.Nickname,
                    Ranking = first.Ranking,
                    HostTime = first.HostTime
                },
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            message = e.Message;
            return await this.HandleExceptionAsync(null, e,
                "API HAS EXCEPTION - ranking [token : {token}, hostTime : {hostTime}, itemCount : {itemCount}]",
                req.Token, req.HostTime, req.ItemCount);
        }
        finally
        {
            await this.WriteLog(LogType.Ranking, userId, roomId, req.ClientVersion, message, dataObject);
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
        this.logger.LogInformation("[POST] /tori/result - [body : {json}]", JsonSerializer.Serialize(req));

        string? userId = null;
        int? roomId = null;
        var message = string.Empty;
        object? dataObject = req;

        try
        {
            var (resultCode, user) = await ValidateToken(req.Token);

            switch (resultCode)
            {
                case ResultCode.Ok:
                    break;

                case ResultCode.InvalidParameter:
                    message = "정상적인 토큰이 아닙니다.";
                    return this.Unauthorized();
                case ResultCode.SessionNotFound:
                    message = "해당 토큰으로 유저의 접속 정보를 확인할 수 없습니다.";
                    return this.Conflict();
                case ResultCode.NotJoinedUser:
                    // 이미 gameend를 호출했으니 이 경우에는 정상
                    break;
                case ResultCode.UnhandledError:
                default:
                    throw new InvalidOperationException(resultCode.ToString());
            }

            userId = user?.Identifier.Id;
            roomId = user?.PlaySession?.RoomId;

            if (user?.PlaySession == null || !user.HasJoined || user.HasQuit)
            {
                message = "유저의 게임 참가 정보가 비정상적입니다.";
                dataObject = new
                {
                    user?.PlaySession?.RoomId,
                    user?.HasJoined,
                    user?.HasLeft,
                };
                return this.Conflict();
            }

            var now = DateTime.UtcNow;
            if (user.PlaySession.CloseAt == null || now < user.PlaySession.CloseAt)
            {
                message = "아직 랭킹 집계가 완료되지 않았습니다.";
                dataObject = new
                {
                    user.PlaySession.CloseAt,
                    now,
                };
                return this.StatusCode(StatusCodes.Status408RequestTimeout);
            }

            resultCode = user.PlaySession.TryGetRanking(user.Identifier, out var first, out var mine);
            if (resultCode != ResultCode.Ok)
            {
                message = "최종 랭킹 조회에 실패했습니다.";
                return this.Conflict();
            }

            message = "최종 랭킹 조회에 성공했습니다.";
            dataObject = new
            {
                first,
                mine,
            };

            return this.Json(new RankingResponse
            {
                MyRank = new RankInfo
                {
                    UserId = mine.Identifier.Id,
                    UserNickname = mine.Identifier.Nickname,
                    Ranking = mine.Ranking,
                    HostTime = mine.HostTime,
                },
                TopRank = new RankInfo
                {
                    UserId = first.Identifier.Id,
                    UserNickname = first.Identifier.Nickname,
                    Ranking = first.Ranking,
                    HostTime = first.HostTime
                },
                CurrentTick = now.Ticks,
            });
        }
        catch (Exception e)
        {
            message = e.Message;
            return await this.HandleExceptionAsync(null, e, "API HAS EXCEPTION - result [token : {token}", req.Token);
        }
        finally
        {
            await this.WriteLog(LogType.Result, userId, roomId, req.ClientVersion, message, dataObject);
        }
    }
}