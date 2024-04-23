using tori.Core;

namespace tori.Sessions;

public class GameSession
{
    public string SessionId { get; }
    public string StageId { get; }

    private SpinLock spinLock;
    private readonly int maxUserLimits;
    
    /// <summary>
    /// Require SpinLock to access
    /// </summary>
    private readonly List<SessionUser> users = new();

    public DateTime CreatedAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameStartAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameEndAt { get; private set; } = DateTime.MaxValue;

    public GameSession(string sessionId, string stageId)
    {
        this.SessionId = sessionId;
        this.StageId = stageId;
        //TODO: 실제 설정을 참조하도록 해야함
        this.maxUserLimits = 50;
    }
    
    public IEnumerable<string> GetNicknames()
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            return this.users.Select(user => user.Identifier.Nickname);
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    public void SetActive()
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            if (this.users.Count != 0) throw new InvalidOperationException();

            var now = DateTime.UtcNow;
            this.CreatedAt = now;
            //TODO: 실제 설정을 참조하도록 해야함
            this.GameStartAt = now.AddMinutes(1);
            this.GameEndAt = now.AddMinutes(6);
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    public bool CanAcceptUser()
    {
        var now = DateTime.UtcNow;

        // 생성되지 않은 방에는 입장 불가
        if (now < this.CreatedAt) return false;
        // 이미 게임이 시작된 방에는 입장 불가
        if (this.GameStartAt <= now) return false;

        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            // 방에 입장 가능한 유저 수 제한을 지켜야 함
            return this.users.Count < this.maxUserLimits;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    public bool IsReusable()
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            if (this.users.Count != 0) return false;

            var now = DateTime.UtcNow;
            
            // 아직 생성 처리가 되지 않은 방은 초기화해서 재사용 가능
            if (now < this.CreatedAt) return true;
            // 게임이 종료된 방은 초기화해서 재사용 가능
            if (this.GameEndAt <= now) return true;
            
            // 게임이 시작되었거나 시작 준비 중인 방은 초기화 불가
            return false;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    /// <summary>
    /// 세션에 새 유저를 입장시킵니다 (SessionManager를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="identifier">입장하는 유저의 식별 정보</param>
    public ResultCode JoinUser(UserIdentifier identifier)
    {
        if (!this.CanAcceptUser()) return ResultCode.UnhandledError;
        
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            if (this.users.Any(u => u.IsSame(identifier))) return ResultCode.AlreadyJoined;

            var user = new SessionUser(identifier);
            this.users.Add(user);
            user.SetSession(this);
            return ResultCode.Ok;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    /// <summary>
    /// 세션에서 유저를 떠나보냅니다 (SessionManager를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="user">떠날 유저</param>
    /// <param name="isQuit">게임이 종료된 것이 아닌 포기일 때 true</param>
    public ResultCode LeaveUser(SessionUser user, bool isQuit = false)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);

            if (user.PlaySession != this) return ResultCode.NotJoinedUser;
            if (user.HasQuit || !user.IsPlaying) return ResultCode.NotJoinedUser;

            if (isQuit) user.HasQuit = true;
            this.users.Remove(user);
            user.SetSession(null);
            return ResultCode.Ok;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    /// <summary>
    /// 세션에 참여 중인 특정 유저를 찾습니다 (SessionManager를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="identifier">찾고 있는 유저의 식별 정보</param>
    /// <param name="user">찾은 유저</param>
    public ResultCode TryGetUser(UserIdentifier identifier, out SessionUser user)
    {
        user = default!;
        
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);

            var found = this.users.FirstOrDefault(u => u.IsSame(identifier));
            if (found == null) return ResultCode.NotJoinedUser;

            user = found;
            return ResultCode.Ok;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }
}