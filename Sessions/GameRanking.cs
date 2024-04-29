namespace tori.Sessions;

public class GameRanking
{
    public class RankItem
    {
        public UserIdentifier Identifier { get; }
        public long JoinAtTick { get; }
        public int Ranking { get; set; }
        public float HostTime { get; set; }
        public int ItemCount { get; set; }

        public RankItem(UserIdentifier userIdentifier, long joinAtTick)
        {
            this.Identifier = userIdentifier;
            this.JoinAtTick = joinAtTick;
        }
    }

    private readonly object locker = new();
    private readonly List<RankItem> ranking = new();
    private readonly Dictionary<UserIdentifier, RankItem> dataMap = new();

    public void Clear()
    {
        lock (this.locker)
        {
            this.ranking.Clear();
            this.dataMap.Clear();
        }
    }
    
    public RankItem GetFirst()
    {
        lock (this.locker)
        {
            return this.ranking.First();
        }
    }

    public bool TryGetRankItem(UserIdentifier userIdentifier, out RankItem? rankItem)
    {
        lock (this.locker)
        {
            return this.dataMap.TryGetValue(userIdentifier, out rankItem);
        }
    }

    public void Register(UserIdentifier userIdentifier, long joinAtTick)
    {
        lock (this.locker)
        {
            if (this.TryGetRankItem(userIdentifier, out var rankItem) && rankItem != null)
                throw new ArgumentException("User duplicated");

            rankItem = new RankItem(userIdentifier, joinAtTick);
            this.ranking.Add(rankItem);
            this.dataMap.Add(userIdentifier, rankItem);
        }
    }

    public RankItem Update(UserIdentifier userIdentifier, float hostTime, int itemCount)
    {
        lock (this.locker)
        {
            if (!this.TryGetRankItem(userIdentifier, out var rankItem) || rankItem == null)
            {
                throw new KeyNotFoundException();
            }

            rankItem.HostTime = hostTime;
            rankItem.ItemCount = itemCount;

            this.ranking.Sort((a, b) =>
            {
                if (Math.Abs(a.HostTime - b.HostTime) < float.Epsilon)
                {
                    // 시간과 아이템 사용 횟수가 모두 동률일 경우, 일찍 참가한 유저가 우승
                    if (a.ItemCount == b.ItemCount)
                        return Comparer<long>.Default.Compare(a.JoinAtTick, b.JoinAtTick);
                    
                    // 시간이 동률일 때 아이템을 더 많이 사용한 유저가 우승
                    return Comparer<int>.Default.Compare(b.ItemCount, a.ItemCount);
                }
                
                // 그 외, 플레이 시간 동안 쿠폰을 가장 오래 가지고 있던 유저
                return Comparer<float>.Default.Compare(b.HostTime, a.HostTime);
            });

            for (int i = 1, len = this.ranking.Count; i <= len; i++)
            {
                this.ranking[i - 1].Ranking = i;
            }

            return rankItem;
        }
    }
}