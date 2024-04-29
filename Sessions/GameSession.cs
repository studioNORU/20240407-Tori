using tori.Core;
using tori.Models;

namespace tori.Sessions;

public class GameSession
{
    public uint SessionId { get; }
    public string StageId { get; }

    private SpinLock spinLock;
    private readonly int maxUserLimits;
    private readonly GameRanking ranking = new();
    
    /// <summary>
    /// Require SpinLock to access
    /// </summary>
    private readonly List<SessionUser> activeUsers = new();
    
    /// <summary>
    /// Require SpinLock to access
    /// </summary>
    private readonly List<SessionUser> users = new();

    public DateTime CreatedAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameStartAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameEndAt { get; private set; } = DateTime.MaxValue;

    public GameSession(uint sessionId, GameStage stage)
    {
        this.SessionId = sessionId;
        this.StageId = stage.StageId;
        this.maxUserLimits = stage.MaxPlayer;
    }
    
    public IEnumerable<string> GetNicknames()
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            return this.activeUsers.Select(user => user.Identifier.Nickname);
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    public void SetActive(GameStage stage)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);
            
            if (this.activeUsers.Count != 0) throw new InvalidOperationException();
            this.activeUsers.Clear();
            this.users.Clear();

            var now = DateTime.UtcNow;
            this.CreatedAt = now;
            this.GameStartAt = now.AddSeconds(Constants.SessionWaitingEntrySeconds);
            this.GameEndAt = this.GameStartAt.AddMilliseconds(stage.Time);
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
            return this.activeUsers.Count < this.maxUserLimits;
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
            
            if (this.activeUsers.Count != 0) return false;

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
            
            if (this.activeUsers.Any(u => u.IsSame(identifier))) return ResultCode.AlreadyJoined;

            var user = new SessionUser(identifier);
            this.activeUsers.Add(user);
            this.users.Add(user);
            user.SetSession(this);
            user.IsPlaying = true;
            user.HasQuit = false;
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
            user.IsPlaying = false;
            this.activeUsers.Remove(user);
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

    public (GameRanking.RankItem first, GameRanking.RankItem mine) UpdateRanking(
        UserIdentifier userIdentifier, float hostTime, int itemCount)
    {
        lock (this.ranking)
        {
            var mine = this.ranking.Update(userIdentifier, hostTime, itemCount);
            var first = this.ranking.GetFirst();

            return (first, mine);
        }
    }

    public ResultCode TryGetRanking(UserIdentifier userIdentifier, out GameRanking.RankItem first, out GameRanking.RankItem mine)
    {
        first = default!;
        mine = default!;
        
        lock (this.ranking)
        {
            if (!this.ranking.TryGetRankItem(userIdentifier, out var found) || found == null)
                return ResultCode.NotJoinedUser;

            mine = found;
            first = this.ranking.GetFirst();
            return ResultCode.Ok;
        }
    }
}