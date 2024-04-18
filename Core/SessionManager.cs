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

    public ResultCode TryJoinUser(UserIdentifier user, out GameSession session)
    {
        session = this.GetActiveSession();
        return session.JoinUser(user);
    }

    public ResultCode TryGetUser(TokenData tokenData, out SessionUser user)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            if (this.sessions.TryGetValue(tokenData.SessionId, out var session))
                return session.TryGetUser(tokenData.User, out user);
            
            user = default!;
            return ResultCode.SessionNotFound;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }
    
    //HACK: 임시로 게임 포기를 구현하기 위한 함수
    public bool TryQuitUser(string userId, out SessionUser user)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            foreach (var session in this.sessions.Values)
            {
                if (session.TryQuitUser(userId, out user))
                    return true;
            }

            user = default!;
            return false;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }
}