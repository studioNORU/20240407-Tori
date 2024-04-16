using tori.Sessions;

namespace tori.Core;

public class SessionManager
{
    private static SessionManager? instance;
    public static SessionManager I => instance ??= new SessionManager();

    private SpinLock spinLock;
    private uint nextSessionId;
    private readonly Dictionary<string, GameSession> sessions = new();

    private GameSession CreateSession()
    {
        var sessionId = $"s{this.nextSessionId++}";
        var session = new GameSession(sessionId, sessionId);
        session.SetActive();
        this.sessions.Add(sessionId, session);
        return session;
    }

    private GameSession GetActiveSession()
    {
        lock (this)
        {
            var session = this.sessions.Values.FirstOrDefault(s => s.CanAcceptUser());
            if (session != null) return session;

            session = this.sessions.Values.FirstOrDefault(s => s.IsReusable());
            if (session != null)
            {
                session.SetActive();
                return session;
            }

            return this.CreateSession();
        }
    }

    public ResultCode TryJoinUser(UserIdentifier user, out GameSession session)
    {
        session = this.GetActiveSession();
        return session.JoinUser(user);
    }
}