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
    /// Require SpinLock to access<br/>
    /// gamestart API 호출 시점 ~ gameend / gamequit API 호출 시점 구간에 해당하는 유저들의 집합입니다.
    /// </summary>
    private readonly List<SessionUser> activeUsers = new();
    
    /// <summary>
    /// Require SpinLock to access<br/>
    /// loading API를 통해 이 방에 진입한 적이 있는 모든 유저들의 집합입니다. (gameend나 gamequit을 통해 이탈해도 방 초기화 이전까지는 이 집합에 남습니다)
    /// </summary>
    private readonly List<SessionUser> users = new();

    public DateTime CreatedAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameStartAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameEndAt { get; private set; } = DateTime.MaxValue;
    public DateTime? CloseAt { get; private set; }

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
            this.ranking.Clear();

            var now = DateTime.UtcNow;
            this.CreatedAt = now;
            this.GameStartAt = now.AddSeconds(Constants.SessionWaitingEntrySeconds);
            this.GameEndAt = this.GameStartAt.AddMilliseconds(stage.Time);
            this.CloseAt = null;
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
            // 집계 완료 후 보류 시간을 넘긴 방은 초기화해서 재사용 가능
            if (this.CloseAt != null && this.CloseAt.Value.AddSeconds(Constants.SessionDeferRecycleSeconds) <= now) return true;
            
            // 게임이 시작되었거나 시작 준비 중인 방, 혹은 집계 완료 후 결과 조회를 위해 보류 중인 방은 초기화 불가
            return false;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    /// <summary>
    /// 플레이가 완료된 세션의 집계 마감 시간을 지정합니다
    /// </summary>
    public void ReserveClose()
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);

            var now = DateTime.UtcNow;

            // 모든 유저의 gameend가 완료되었다면 바로 집계 마감
            // 혹은, 집계 강제 마감 시간이 설정되지 않았다면 설정
            if (this.activeUsers.Count <= 0) this.CloseAt = now;
            else this.CloseAt ??= now.AddSeconds(Constants.SessionForceCloseSeconds);
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    /// <summary>
    /// 세션에 새 유저를 입장시킵니다 (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
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

            var user = this.users.FirstOrDefault(u => u.IsSame(identifier));
            if (user == null)
            {
                user = new SessionUser(identifier, DateTime.UtcNow);
                this.users.Add(user);
            }
            
            user.SetSession(this);
            user.HasQuit = false;
            user.HasLeft = false;
            user.HasJoined = true;
            return ResultCode.Ok;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    /// <summary>
    /// 유저가 본 세션에서 플레이를 시작합니다 (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="user">플레이를 시작한 유저</param>
    public ResultCode Start(SessionUser user)
    {
        var lockTaken = false;
        try
        {
            this.spinLock.Enter(ref lockTaken);

            if (!user.HasJoined) return ResultCode.NotJoinedUser;
            if (!this.users.Any(x => x.IsSame(user))) return ResultCode.NotJoinedUser;
            if (this.activeUsers.Any(x => x.IsSame(user))) return ResultCode.AlreadyJoined;
            
            this.activeUsers.Add(user);
            this.ranking.Register(user.Identifier, user.JoinedAt.Ticks);
            user.IsPlaying = true;
            return ResultCode.Ok;
        }
        finally
        {
            if (lockTaken) this.spinLock.Exit();
        }
    }

    /// <summary>
    /// 세션에서 유저를 떠나보냅니다 (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
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
            user.HasLeft = true;
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
    /// 세션에 참여 중인 특정 유저를 찾습니다 (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
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

    /// <summary>
    /// 유저의 랭킹 정보를 갱신합니다
    /// </summary>
    /// <param name="userIdentifier">랭킹 정보를 갱신할 유저 식별 정보</param>
    /// <param name="hostTime">유저의 점수</param>
    /// <param name="itemCount">유저의 아이템 사용 횟수</param>
    public (GameRanking.RankItem first, GameRanking.RankItem mine) UpdateRanking(
        UserIdentifier userIdentifier, float hostTime, int itemCount)
    {
        var mine = this.ranking.Update(userIdentifier, hostTime, itemCount);
        var first = this.ranking.GetFirst();

        return (first, mine);
    }

    /// <summary>
    /// 유저와 방 내 1위의 랭킹 정보를 조회합니다.
    /// </summary>
    /// <param name="userIdentifier">랭킹 정보를 조회할 유저 식별 정보</param>
    /// <param name="first">방 내 1위의 랭킹 정보</param>
    /// <param name="mine">유저의 랭킹 정보</param>
    public ResultCode TryGetRanking(UserIdentifier userIdentifier, out GameRanking.RankItem first, out GameRanking.RankItem mine)
    {
        first = default!;
        mine = default!;
        
        if (!this.ranking.TryGetRankItem(userIdentifier, out var found) || found == null)
            return ResultCode.NotJoinedUser;

        mine = found;
        first = this.ranking.GetFirst();
        return ResultCode.Ok;
    }
}