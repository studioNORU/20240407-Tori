namespace tori.Sessions;

public class GameRanking
{
    public class RankItem
    {
        public UserIdentifier Identifier { get; }
        public int Ranking { get; set; }
        public float HostTime { get; set; }
        public int ItemCount { get; set; }

        public RankItem(UserIdentifier userIdentifier)
        {
            this.Identifier = userIdentifier;
        }
    }

    private readonly object locker = new();
    private readonly List<RankItem> ranking = new();
    private readonly Dictionary<UserIdentifier, RankItem> dataMap = new();
    
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

    public RankItem Update(UserIdentifier userIdentifier, float hostTime, int itemCount)
    {
        lock (this.locker)
        {
            if (!this.TryGetRankItem(userIdentifier, out var rankItem) || rankItem == null)
            {
                rankItem = new RankItem(userIdentifier);
                this.ranking.Add(rankItem);
                this.dataMap.Add(userIdentifier, rankItem);
            }

            rankItem.HostTime = hostTime;
            rankItem.ItemCount = itemCount;

            this.ranking.Sort((a, b) =>
            {
                // 시간이 동률일 때 아이템을 더 많이 사용한 유저가 우승
                if (Math.Abs(a.HostTime - b.HostTime) < float.Epsilon)
                    return Comparer<int>.Default.Compare(b.ItemCount, a.ItemCount);
                
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