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
    private uint nextSessionId;
    
    /// <summary>
    /// Require SpinLock to access
    /// </summary>
    private readonly Dictionary<string, GameSession> sessions = new();

    private GameSession GetActiveSession()
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
                session.SetActive();
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
            var sessionId = $"s{this.nextSessionId++}";
            var session = new GameSession(sessionId, sessionId);
            session.SetActive();
            this.sessions.Add(sessionId, session);
            return session;
        }
    }

    /// <summary>
    /// 세션에 새 유저를 입장시킵니다
    /// </summary>
    /// <param name="identifier">입장하는 유저의 식별 정보</param>
    /// <param name="session">유저가 입장한 세션</param>
    public ResultCode TryJoin(UserIdentifier identifier, out GameSession session)
    {
        session = this.GetActiveSession();
        return session.JoinUser(identifier);
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