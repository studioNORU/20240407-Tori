namespace tori.Sessions;

public class SessionUser
{
    public readonly UserIdentifier Identifier;

    public int ItemUsages { get; set; }
    public int HostTime { get; set; }
    public GameSession? PlaySession { get; private set; }
    public bool HasQuit { get; set; }
    public bool IsPlaying { get; private set; }

    public SessionUser(UserIdentifier identifier)
    {
        this.Identifier = identifier;
        this.ItemUsages = 0;
        this.HostTime = 0;
    }

    /// <summary>
    /// <see cref="GameSession"/>에서만 사용해야 합니다
    /// </summary>
    public void SetSession(GameSession? session)
    {
        this.IsPlaying = session != null;
        this.PlaySession = session;
    }

    public bool IsSame(SessionUser user)
        => this.IsSame(user.Identifier);

    public bool IsSame(UserIdentifier user)
        => this.Identifier.Id == user.Id;

    public override bool Equals(object? obj) =>
        obj switch
        {
            SessionUser user => this.Identifier.Id == user.Identifier.Id,
            UserIdentifier identifier => this.Identifier.Id == identifier.Id,
            _ => false
        };

    public override int GetHashCode() => this.Identifier.Id.GetHashCode();
}

public record UserIdentifier(string Id, string Nickname);
