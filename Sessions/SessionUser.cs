#pragma warning disable CS8618
namespace tori.Sessions;

public class SessionUser
{
    public readonly UserIdentifier Identifier;

    public uint ItemUsages { get; private set; }
    public ulong HostTime { get; private set; }
    public GameSession? PlaySession { get; private set; }
    public bool HasQuit { get; private set; }
    public bool IsPlaying { get; private set; }

    public SessionUser(UserIdentifier identifier)
    {
        this.Identifier = identifier;
        this.ItemUsages = 0U;
        this.HostTime = 0UL;
    }

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
