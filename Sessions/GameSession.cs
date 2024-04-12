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

    public ResultCode JoinUser(UserIdentifier identifier)
    {
        if (!this.CanAcceptUser()) return ResultCode.UnhandledError;
        if (this.users.Any(u => u.Identifier == identifier)) return ResultCode.AlreadyJoined;

        var user = new SessionUser(identifier);
        this.users.Add(user);
        return ResultCode.Ok;
    }
}