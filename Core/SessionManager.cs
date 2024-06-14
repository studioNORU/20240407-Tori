using tori.AppApi.Model;
using tori.Models;
using tori.Sessions;

namespace tori.Core;

public class SessionManager
{
    private static SessionManager? instance;
    public static SessionManager I => instance ??= new SessionManager();

    private readonly SemaphoreSlim semaphoreSlim = new(1, 1);
    
    /// <summary>
    /// Require SemaphoreSlim to access
    /// </summary>
    private readonly Dictionary<int, GameSession> sessions = new();
    
    private static GameStage SelectStage(AppDbContext dbContext)
    {
        return dbContext.GameStages
            .ToList()
            .OrderBy(x => Random.Shared.Next())
            .First();
    }
    
    private GameSession GetActiveSession(AppDbContext dbContext, RoomInfo roomInfo)
    {
        try
        {
            this.semaphoreSlim.Wait();

            if (!this.sessions.TryGetValue(roomInfo.RoomId, out var session))
            {
                session = this.sessions.Values.FirstOrDefault(s => s.IsReusable());
                if (session == null)
                {
                    session = new GameSession();
                }
                else
                {
                    this.sessions.Remove(session.RoomId);
                }

                InitSession(session);
                this.sessions.Add(roomInfo.RoomId, session);
                return session;
            }
            
            if (session.IsReusable())
            {
                InitSession(session);
                return session;
            }

            return session;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
        
        void InitSession(GameSession session)
        {
            var stage = SelectStage(dbContext);
            session.SetActive(roomInfo, stage);
        }
    }

    public GameSession? GetResumableSession(AppDbContext dbContext, UserIdentifier identifier)
    {
        try
        {
            this.semaphoreSlim.Wait();

            var gameUser = dbContext.GameUsers.LastOrDefault(u =>
                u.UserId == identifier.UserId
                && (u.Status == PlayStatus.Playing
                    || u.Status == PlayStatus.Done));

            if (gameUser == null || !this.sessions.TryGetValue(gameUser.RoomId, out var session)) return null;
            
            // 이 방이 초기화 후 재사용을 위해 대기 중인 상태라면 여기서 재개할 수 없습니다
            if (session.IsReusable())
                return null;
            
            // 이 방에서 유저 정보를 찾지 못했다면 여기서 재개할 수 없습니다
            if (session.TryGetUser(identifier, out var user) != ResultCode.Ok)
                return null;
            
            // 이 방에서 조회한 유저 정보로는 방에 진입한 기록을 찾을 수 없거나 포기한 것으로 확인되면 여기서 재개할 수 없습니다
            if (!user.HasJoined || user.HasQuit)
                return null;
            
            // 이 방에서 조회한 유저 정보로 할당된 방 정보를 확인할 수 없거나 이미 게임이 종료되었다면 여기서 재개할 수 없습니다
            var now = DateTime.UtcNow;
            if (user.PlaySession == null || user.PlaySession.CloseAt <= now)
                return null;

            // 이 방에서 게임을 재개할 수 있다면 방 정보를 전달합니다
            return session;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    /// <summary>
    /// 세션에 새 유저를 입장시킵니다
    /// </summary>
    /// <param name="identifier">입장하는 유저의 식별 정보</param>
    /// <param name="roomInfo">입장할 방 정보</param>
    /// <param name="dbContext">DB 컨텍스트</param>
    /// <param name="user">입장한 유저 정보</param>
    /// <param name="session">유저가 입장한 세션</param>
    public ResultCode TryJoin(UserIdentifier identifier, RoomInfo roomInfo, AppDbContext dbContext, out SessionUser? user, out GameSession session)
    {
        session = this.GetResumableSession(dbContext, identifier) ?? this.GetActiveSession(dbContext, roomInfo);
        return session.JoinUser(identifier, out user);
    }

    public GameSession? GetSession(int roomId)
    {
        return this.sessions.TryGetValue(roomId, out var session) ? session : null;
    }
    
    /// <summary>
    /// 유저가 세션에서 플레이를 시작합니다
    /// </summary>
    /// <param name="user">플레이를 시작한 유저</param>
    public ResultCode Start(SessionUser user)
    {
        try
        {
            this.semaphoreSlim.Wait();

            if (user.PlaySession == null || !this.sessions.ContainsValue(user.PlaySession))
                return ResultCode.SessionNotFound;

            return user.PlaySession.Start(user);
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }
    
    /// <summary>
    /// 세션에서 유저를 떠나보냅니다
    /// </summary>
    /// <param name="user">떠날 유저</param>
    /// <param name="isQuit">게임이 종료된 것이 아닌 포기일 때 true</param>
    public ResultCode TryLeave(SessionUser user, bool isQuit)
    {
        try
        {
            this.semaphoreSlim.Wait();

            if (user.PlaySession == null || !this.sessions.ContainsValue(user.PlaySession))
                return ResultCode.SessionNotFound;

            return user.PlaySession.LeaveUser(user, isQuit);
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    /// <summary>
    /// 토큰 정보로 유저를 찾습니다
    /// </summary>
    /// <param name="tokenData">토큰 정보</param>
    /// <param name="user">찾은 유저</param>
    public ResultCode TryGetUser(TokenData tokenData, out SessionUser user)
    {
        user = default!;
        try
        {
            this.semaphoreSlim.Wait();
            
            if (this.sessions.TryGetValue(tokenData.RoomId, out var session))
                return session.TryGetUser(tokenData.User, out user);
            
            return ResultCode.SessionNotFound;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    /// <summary>
    /// 유저 ID와 게임 방 ID로 유저를 찾습니다
    /// </summary>
    /// <param name="userId">유저 ID</param>
    /// <param name="roomId">게임 방 ID</param>
    public SessionUser? TryGetUser(int userId, int roomId)
    {
        try
        {
            this.semaphoreSlim.Wait();

            if (this.sessions.TryGetValue(roomId, out var session))
                return session.TryGetUser(userId);

            return null;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    /// <summary>
    /// 일정 시간 비활성화 상태인 (게임 기록 API가 호출되지 않은) 유저의 연결을 끊습니다.
    /// </summary>
    /// <param name="dbContext">DB 컨텍스트</param>
    /// <param name="dataFetcher">데이터 관리를 위한 인스턴스</param>
    /// <param name="inactivityThreshold">연결을 끊을 비활성화 상태 지속 시간</param>
    /// <returns>연결이 끊긴 비활성화 유저의 수</returns>
    public int DisconnectInactiveUsers(AppDbContext dbContext, DataFetcher dataFetcher, TimeSpan inactivityThreshold)
    {
        var disconnected = 0;
        var now = DateTime.UtcNow;
        try
        {
            this.semaphoreSlim.Wait();

            foreach (var session in this.sessions.Values)
            {
                disconnected += session.DisconnectInactiveUsers(dbContext, dataFetcher, now, inactivityThreshold);
            }
        }
        finally
        {
            this.semaphoreSlim.Release();
        }

        return disconnected;
    }

    /// <summary>
    /// 게임 결과를 앱 서버로 보냅니다.
    /// </summary>
    /// <param name="dbContext">DB 컨텍스트</param>
    /// <param name="dataFetcher">데이터 관리를 위한 인스턴스</param>
    public async Task<IReadOnlyList<GameSession>> PostGameResult(AppDbContext dbContext, DataFetcher dataFetcher)
    {
        var now = DateTime.UtcNow;
        var sent = new List<GameSession>();
        try
        {
            await this.semaphoreSlim.WaitAsync();

            foreach (var session in this.sessions.Values)
            {
                if (session.SentResult) continue;
                if (session.CloseAt == null || now < session.CloseAt) continue;

                await session.SendResult(dbContext, dataFetcher);
                if (session.SentResult) sent.Add(session);
            }

            return sent;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }
}