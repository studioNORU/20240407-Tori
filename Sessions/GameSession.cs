using tori.Core;

namespace tori.Sessions;

public class GameSession
{
    public string SessionId { get; private set; }
    public string StageId { get; private set; }

    private SpinLock spinLock;
    private readonly int maxUserLimits;
    private readonly List<SessionUser> users = new();

    public DateTime CreatedAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameStartAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameEndAt { get; private set; } = DateTime.MaxValue;

    public IEnumerable<string> GetNicknames() => this.users.Select(user => user.Identifier.Nickname);

    public GameSession(string sessionId, string stageId)
    {
        this.SessionId = sessionId;
        this.StageId = stageId;
        //TODO: 실제 설정을 참조하도록 해야함
        this.maxUserLimits = 50;
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
            return ResultCode.Ok;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

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
    
    //HACK: 임시로 게임 포기를 구현하기 위한 함수
    public bool TryQuitUser(string userId, out SessionUser user)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            user = default!;
            var found = this.users.FirstOrDefault(u => u.Identifier.Id == userId);
            if (found == null) return false;

            this.users.Remove(found);
            found.SetSession(null);
            user = found;
            return true;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }
}