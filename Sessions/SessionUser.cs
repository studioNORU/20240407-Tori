#pragma warning disable CS8618
namespace tori.Sessions;

public class SessionUser
{
    public readonly UserIdentifier Identifier;

    public uint ItemUsages { get; private set; }
    public ulong HostTime { get; private set; }

    public SessionUser(UserIdentifier identifier)
    {
        this.Identifier = identifier;
        this.ItemUsages = 0U;
        this.HostTime = 0UL;
    }

    public override bool Equals(object? obj) =>
        obj switch
        {
            SessionUser user => this.Identifier == user.Identifier,
            UserIdentifier identifier => this.Identifier == identifier,
            _ => false
        };

    public override int GetHashCode() => this.Identifier.GetHashCode();
}

public record UserIdentifier
{
    public string PlayerId { get; init; }
    public string Nickname { get; init; }
}