using System.Text.Json.Serialization;

namespace Tori.Controllers.Data;

public record UserStatus(
    int UserId,
    Dictionary<int, int> SpentItems,
    int SpentEnergy,
    [property: JsonIgnore] DateTime Timestamp)
{
    public UserStatus Delta(UserStatus after)
    {
        var spentItems = after.SpentItems.ToDictionary();
        foreach (var iter in this.SpentItems)
        {
            spentItems[iter.Key] -= iter.Value;
        }

        return new UserStatus(this.UserId, spentItems, after.SpentEnergy - this.SpentEnergy, after.Timestamp);
    }
}