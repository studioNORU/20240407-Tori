using Microsoft.EntityFrameworkCore;
using tori.Models;
using tori.Sessions;

namespace tori.Core;

public class SessionManager
{
    private static SessionManager? instance;
    public static SessionManager I => instance ??= new SessionManager();

    private SpinLock spinLock;
    
    /// <summary>
    /// Require SpinLock to access
    /// </summary>
    private uint nextSessionId = 1;
    
    /// <summary>
    /// Require SpinLock to access
    /// </summary>
    private readonly Dictionary<uint, GameSession> sessions = new();
    
    public static GameStage SelectStage(AppDbContext dbContext)
    {
        return dbContext.GameStages
            .ToList()
            .OrderBy(x => Random.Shared.Next())
            .First();
    }
    
    private GameSession GetActiveSession(AppDbContext dbContext)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            var session = this.sessions.Values.FirstOrDefault(s => s.CanAcceptUser());
            if (session != null) return session;

            session = this.sessions.Values.FirstOrDefault(s => s.IsReusable());
            if (session != null)
            {
                var stage = SelectStage(dbContext);
                session.SetActive(stage);
                return session;
            }

            return CreateSession();
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
        
        GameSession CreateSession()
        {
            var sessionId = this.nextSessionId++;
            var stage = SelectStage(dbContext);
            var session = new GameSession(sessionId, stage);
            session.SetActive(stage);
            this.sessions.Add(sessionId, session);
            return session;
        }
    }

    private GameSession? GetResumableSession(UserIdentifier identifier)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);

            foreach (var session in this.sessions.Values)
            {
                var now = DateTime.UtcNow;
                
                // 이 방이 초기화 후 재사용을 위해 대기 중인 상태라면 다른 방을 확인합니다
                if (session.IsReusable())
                    continue;
                
                // 이 방에서 유저 정보를 찾지 못했다면 다른 방을 확인합니다
                if (session.TryGetUser(identifier, out var user) != ResultCode.Ok)
                    continue;
                
                // 이 방에서 조회한 유저 정보로는 방에 진입한 기록을 찾을 수 없거나 포기한 것으로 확인되면 다른 방을 확인합니다
                if (!user.HasJoined || user.HasQuit)
                    continue;
                
                // 이 방에서 조회한 유저 정보로 할당된 방 정보를 확인할 수 없거나 이미 게임이 종료되었다면 다른 방을 확인합니다
                if (user.PlaySession == null || user.PlaySession.CloseAt <= now)
                    continue;

                // 이 방에서 게임을 재개할 수 있다면 방 정보를 전달합니다
                return session;
            }
            
            // 게임을 재개할 수 있는 방을 찾지 못했다면 null을 반환합니다
            return null;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    /// <summary>
    /// 세션에 새 유저를 입장시킵니다
    /// </summary>
    /// <param name="identifier">입장하는 유저의 식별 정보</param>
    /// <param name="dbContext">DB 컨텍스트</param>
    /// <param name="session">유저가 입장한 세션</param>
    public ResultCode TryJoin(UserIdentifier identifier, AppDbContext dbContext, out GameSession session)
    {
        session = this.GetResumableSession(identifier) ?? this.GetActiveSession(dbContext);
        return session.JoinUser(identifier);
    }

    /// <summary>
    /// 유저가 세션에서 플레이를 시작합니다
    /// </summary>
    /// <param name="user">플레이를 시작한 유저</param>
    public ResultCode Start(SessionUser user)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);

            if (user.PlaySession == null || !this.sessions.ContainsValue(user.PlaySession))
                return ResultCode.SessionNotFound;

            return user.PlaySession.Start(user);
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }
    
    /// <summary>
    /// 세션에서 유저를 떠나보냅니다
    /// </summary>
    /// <param name="user">떠날 유저</param>
    /// <param name="isQuit">게임이 종료된 것이 아닌 포기일 때 true</param>
    public ResultCode TryLeave(SessionUser user, bool isQuit)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);

            if (user.PlaySession == null || !this.sessions.ContainsValue(user.PlaySession))
                return ResultCode.SessionNotFound;

            return user.PlaySession.LeaveUser(user, isQuit);
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
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
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            if (this.sessions.TryGetValue(tokenData.SessionId, out var session))
                return session.TryGetUser(tokenData.User, out user);
            
            return ResultCode.SessionNotFound;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }
}