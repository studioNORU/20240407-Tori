using tori.Core;

namespace tori.Sessions;

public class GameSession
{
    public string SessionId { get; private set; }
    public string StageId { get; private set; }

    private SpinLock spinLock = new();
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
        if (this.users.Count != 0) throw new InvalidOperationException();
        
        var now = DateTime.UtcNow;
        this.CreatedAt = now;
        //TODO: 실제 설정을 참조하도록 해야함
        this.GameStartAt = now.AddMinutes(1);
        this.GameEndAt = now.AddMinutes(6);
    }

    public bool CanAcceptUser()
    {
        var now = DateTime.UtcNow;

        // 생성되지 않은 방에는 입장 불가
        if (now < this.CreatedAt) return false;
        // 이미 게임이 시작된 방에는 입장 불가
        if (this.GameStartAt <= now) return false;
        
        // 방에 입장 가능한 유저 수 제한을 지켜야 함
        return this.users.Count < this.maxUserLimits;
    }

    public bool IsReusable()
    {
        if (this.users.Count != 0) return false;

        var now = DateTime.UtcNow;
        
        // 아직 생성 처리가 되지 않은 방은 초기화해서 재사용 가능
        if (now < this.CreatedAt) return true;
        // 게임이 종료된 방은 초기화해서 재사용 가능
        if (this.GameEndAt <= now) return true;
        
        // 게임이 시작되었거나 시작 준비 중인 방은 초기화 불가
        return false;
    }

    public ResultCode JoinUser(UserIdentifier identifier)
    {
        if (!this.CanAcceptUser()) return ResultCode.UnhandledError;
        if (this.users.Any(u => u.IsSame(identifier))) return ResultCode.AlreadyJoined;

        var user = new SessionUser(identifier);
        this.users.Add(user);
        return ResultCode.Ok;
    }
}