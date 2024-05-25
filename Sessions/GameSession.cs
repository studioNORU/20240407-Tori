using Microsoft.EntityFrameworkCore;
using tori.AppApi.Model;
using tori.Core;
using tori.Models;

namespace tori.Sessions;

public class GameSession
{
    public int RoomId => this.roomInfo.RoomId;
    public string StageId { get; private set; } = default!;

    private readonly SemaphoreSlim semaphoreSlim = new(1, 1);
    private RoomInfo roomInfo = default!;
    private readonly GameRanking ranking = new();
    
    /// <summary>
    /// Require SemaphoreSlim to access<br/>
    /// gamestart API 호출 시점 ~ gameend / gamequit API 호출 시점 구간에 해당하는 유저들의 집합입니다.
    /// </summary>
    private readonly List<SessionUser> activeUsers = new();
    
    /// <summary>
    /// Require SemaphoreSlim to access<br/>
    /// loading API를 통해 이 방에 진입한 적이 있는 모든 유저들의 집합입니다. (gameend나 gamequit을 통해 이탈해도 방 초기화 이전까지는 이 집합에 남습니다)
    /// </summary>
    private readonly List<SessionUser> users = new();

    public DateTime CreatedAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameStartAt { get; private set; } = DateTime.MaxValue;
    public DateTime GameEndAt { get; private set; } = DateTime.MaxValue;
    public DateTime? CloseAt { get; private set; }
    public bool SentResult { get; private set; }
    
    public IEnumerable<string> GetNicknames()
    {
        try
        {
            this.semaphoreSlim.Wait();
            return this.activeUsers.Select(user => user.Identifier.Nickname);
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    public void SetActive(RoomInfo info, GameStage stage)
    {
        try
        {
            this.semaphoreSlim.Wait();
            
            this.roomInfo = info;
            this.StageId = stage.StageId;
            
            if (this.activeUsers.Count != 0) throw new InvalidOperationException();
            this.activeUsers.Clear();
            this.users.Clear();
            this.ranking.Clear();

            var now = DateTime.UtcNow;
            this.CreatedAt = now;
            this.GameStartAt = info.BeginRunningTime;
            this.GameEndAt = this.roomInfo.EndRunningTime;
            this.CloseAt = null;
            this.SentResult = false;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    public bool IsReusable()
    {
        try
        {
            this.semaphoreSlim.Wait();
            
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
            this.semaphoreSlim.Release();
        }
    }

    /// <summary>
    /// 플레이가 완료된 세션의 집계 마감 시간을 지정합니다
    /// </summary>
    public void ReserveClose()
    {
        try
        {
            this.semaphoreSlim.Wait();

            var now = DateTime.UtcNow;

            // 모든 유저의 gameend가 완료되었다면 바로 집계 마감
            // 혹은, 집계 강제 마감 시간이 설정되지 않았다면 설정
            if (this.activeUsers.Count <= 0) this.CloseAt = now;
            else this.CloseAt ??= now.AddSeconds(Constants.SessionForceCloseSeconds);
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    /// <summary>
    /// 세션에 새 유저를 입장시킵니다 (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="identifier">입장하는 유저의 식별 정보</param>
    /// <param name="user">입장한 유저 정보</param>
    public ResultCode JoinUser(UserIdentifier identifier, out SessionUser? user)
    {
        try
        {
            this.semaphoreSlim.Wait();

            user = null;
            if (this.activeUsers.Any(u => u.IsSame(identifier))) return ResultCode.AlreadyJoined;

            user = this.users.FirstOrDefault(u => u.IsSame(identifier));
            
            // 신규 유저
            if (user == null && InternalCanAcceptUser())
            {
                user = new SessionUser(identifier, DateTime.UtcNow);
                this.users.Add(user);
            }
            // gameend 이후 다시 재개하는 유저
            else if (user != null)
            {
                // gamestart 호출 가능한 시점 이후라면 관련 처리 진행
                if (this.GameStartAt <= DateTime.UtcNow)
                {
                    if (!this.ranking.TryGetRankItem(user.Identifier, out var rankItem) || rankItem == null)
                        this.ranking.Register(user.Identifier, user.JoinedAt.Ticks);
                    
                    this.activeUsers.Add(user);
                    user.IsPlaying = true;
                }
            }
            // 그 외의 경우에는 진입 불가
            else return ResultCode.UnhandledError;
            
            user.SetSession(this);
            user.HasQuit = false;
            user.HasLeft = false;
            user.HasJoined = true;
            return ResultCode.Ok;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
        
        bool InternalCanAcceptUser()
        {
            var now = DateTime.UtcNow;

            // 생성되지 않은 방에는 입장 불가
            if (now < this.CreatedAt) return false;
            // 이미 게임이 시작된 방에는 입장 불가
            if (this.GameStartAt <= now) return false;

            return this.activeUsers.Count < this.roomInfo.PlayerCount;
        }
    }

    /// <summary>
    /// 유저가 본 세션에서 플레이를 시작합니다 (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="user">플레이를 시작한 유저</param>
    public ResultCode Start(SessionUser user)
    {
        try
        {
            this.semaphoreSlim.Wait();

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
            this.semaphoreSlim.Release();
        }
    }

    /// <summary>
    /// 세션에서 유저를 떠나보냅니다 (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="user">떠날 유저</param>
    /// <param name="isQuit">게임이 종료된 것이 아닌 포기일 때 true</param>
    public ResultCode LeaveUser(SessionUser user, bool isQuit = false)
    {
        try
        {
            this.semaphoreSlim.Wait();

            if (user.PlaySession != this) return ResultCode.NotJoinedUser;
            if (user.HasLeft) return ResultCode.NotJoinedUser;

            this.InternalLeaveUser(user, isQuit);
            return ResultCode.Ok;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    private void InternalLeaveUser(SessionUser user, bool isQuit = false)
    {
        if (isQuit) user.HasQuit = true;
        user.HasLeft = true;
        user.IsPlaying = false;
        this.activeUsers.Remove(user);
    }

    /// <summary>
    /// 세션에 참여 중인 특정 유저를 찾습니다 (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="identifier">찾고 있는 유저의 식별 정보</param>
    /// <param name="user">찾은 유저</param>
    public ResultCode TryGetUser(UserIdentifier identifier, out SessionUser user)
    {
        user = default!;
        
        try
        {
            this.semaphoreSlim.Wait();

            var found = this.users.FirstOrDefault(u => u.IsSame(identifier));
            if (found == null) return ResultCode.NotJoinedUser;

            user = found;
            return ResultCode.Ok;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }
    
    /// <summary>
    /// 세션에 참여 중인 특정 유저를 찾습니다 (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="userId">찾고 있는 유저의 ID</param>
    public SessionUser? TryGetUser(int userId)
    {
        try
        {
            this.semaphoreSlim.Wait();

            return this.users.FirstOrDefault(u => u.UserId == userId);
        }
        finally
        {
            this.semaphoreSlim.Release();
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

    private bool DisconnectInactiveUser(AppDbContext dbContext, DataFetcher dataFetcher, SessionUser user, DateTime now)
    {
        var gameUser = dbContext.GameUsers
            .Include(u => u.PlayData)
            .SingleOrDefault(u =>
                u.UserId == user.UserId
                && u.RoomId == this.RoomId);

        if (gameUser == null) return false;
        if (gameUser.Status != PlayStatus.Playing) return false;
                
        this.InternalLeaveUser(user, isQuit: true);
                
        gameUser.Status = PlayStatus.Disconnected;
        gameUser.LeavedAt = now;
        dbContext.GameUsers.Update(gameUser);
        dbContext.SaveChanges();

        var spentItems = gameUser.PlayData?.SpentItems ?? new Dictionary<int, int>();
        var timestamp = gameUser.PlayData?.TimeStamp ?? now;
        _ = dataFetcher.UpdateUserStatus(user, spentItems, timestamp);
        return true;
    }
    
    /// <summary>
    /// 일정 시간 비활성화 상태인 (게임 기록 API가 호출되지 않은) 유저의 연결을 끊습니다. (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="dbContext">DB 컨텍스트</param>
    /// <param name="dataFetcher">데이터 관리를 위한 인스턴스</param>
    /// <param name="now">현재 시간 (UTC)</param>
    /// <param name="inactivityThreshold">연결을 끊을 비활성화 상태 지속 시간</param>
    /// <returns>연결이 끊긴 비활성화 유저의 수</returns>
    public int DisconnectInactiveUsers(AppDbContext dbContext, DataFetcher dataFetcher, DateTime now, TimeSpan inactivityThreshold)
    {
        var disconnected = 0;
        try
        {
            this.semaphoreSlim.Wait();

            var inactiveUsers = this.activeUsers
                .Where(u => inactivityThreshold <= now - u.LastActiveAt)
                .ToArray();

            foreach (var user in inactiveUsers)
            {
                if (user.PlaySession != this) continue;
                if (user.HasLeft) continue;

                if (this.DisconnectInactiveUser(dbContext, dataFetcher, user, now))
                    disconnected++;
            }
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
        return disconnected;
    }

    /// <summary>
    /// 게임 결과를 앱 서버로 전송합니다. (<see cref="SessionManager">SessionManager</see>를 통해 접근해야 합니다)
    /// </summary>
    /// <param name="dbContext">DB 컨텍스트</param>
    /// <param name="dataFetcher">데이터 관리를 위한 인스턴스</param>
    public async Task SendResult(AppDbContext dbContext, DataFetcher dataFetcher)
    {
        try
        {
            await this.semaphoreSlim.WaitAsync();

            var userIds = this.users.Select(u => u.UserId).ToArray();
            var first = this.ranking.GetFirst();
            var firstPlayData = await dbContext.PlayData.SingleOrDefaultAsync(p =>
                p.UserId == first.Identifier.UserId
                && p.RoomId == this.RoomId);
            var spentItems = firstPlayData?.SpentItems ?? new Dictionary<int, int>();

            await dataFetcher.SendResult(
                this.roomInfo,
                new GameResult(
                    this.RoomId,
                    userIds,
                    new GameResultFirst(
                        first.Identifier.UserId,
                        spentItems,
                        first.HostTime)));
            
            this.SentResult = true;
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }
}