using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using tori.AppApi;
using tori.AppApi.Model;
using Tori.Controllers.Data;
using tori.Models;
using tori.Sessions;

namespace tori.Core;

public class DataFetcher
{
    private readonly IServiceProvider serviceProvider;

    public DataFetcher(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public async Task<RoomInfo?> GetRoomInfo(int roomId)
    {
        await using var scope = this.serviceProvider.CreateAsyncScope();
    #if !RELEASE
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var test = await dbContext.TestRooms.SingleOrDefaultAsync(r => r.RoomId == roomId);
        if (test != null) return test.ToRoomInfo();
    #endif
        
        var apiClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
        return await apiClient.GetAsync<RoomInfo>(API_URL.RoomInfo, new Dictionary<string, string>
        {
            { "roomId", roomId.ToString() },
        });
    }

    public async Task<UserInfo?> GetUserInfo(string userIdStr)
    {
        await using var scope = this.serviceProvider.CreateAsyncScope();
    #if !RELEASE
        var userId = int.Parse(userIdStr);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var test = await dbContext.TestUsers.SingleOrDefaultAsync(u => u.Id == userId);
        if (test != null) return test.ToUserInfo();
    #endif
        
        var apiClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
        return await apiClient.GetAsync<UserInfo>(API_URL.UserInfo, new Dictionary<string, string>
        {
            { "userNo", userIdStr },
        });
    }

#if !RELEASE
    private async Task UpdateTestUser(int userId, int spentEnergy, UserStatus delta)
    {
        await using var scope = this.serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var test = await dbContext.TestUsers.SingleAsync(u => u.Id == userId);
        if (test.Energy < spentEnergy) return;

        var inventory = JsonSerializer.Deserialize<ItemInfo[]>(test.InventoryJson)!
            .ToDictionary(it => it.ItemNo, it => it.ItemCount);
        foreach (var (item, amount) in delta.SpentItems)
        {
            if (!inventory.TryGetValue(item, out var value)) return;
            if (value < amount) return;
                
            inventory[item] -= amount;
        }

        test.InventoryJson = JsonSerializer.Serialize(inventory
            .Select(it => new ItemInfo(it.Key, it.Value))
            .ToArray());
        test.Energy -= delta.SpentEnergy;
            
        dbContext.TestUsers.Update(test);
        await dbContext.SaveChangesAsync();
    }
#endif
    
    public async Task UpdateUserStatus(SessionUser user, Dictionary<int, int> spentItems, DateTime timestamp)
    {
        await using var scope = this.serviceProvider.CreateAsyncScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
        
        var playTime = timestamp - user.PlaySession!.GameStartAt;
        var spentEnergy = (int)Math.Floor(playTime.TotalMinutes) * Constants.EnergyCostPerMinutes;
        var curStatus = new UserStatus(
            user.UserId,
            spentItems,
            spentEnergy,
            timestamp);
        var delta = curStatus;

        if (user.CachedStatus != null) delta = user.CachedStatus.Delta(curStatus);

        user.CachedStatus = curStatus;

#if !RELEASE
        if (user.UserInfo is TestUserInfo)
        {
            await this.UpdateTestUser(user.UserId, spentEnergy, delta);
            return;
        }
#endif
        
        await apiClient.PostAsync(API_URL.UserStatus, new StringContent(
            JsonSerializer.Serialize(delta),
            Encoding.UTF8,
            "application/json"));
    }

    public async Task SendResult(RoomInfo roomInfo, GameResult gameResult)
    {
        await using var scope = this.serviceProvider.CreateAsyncScope();
        
#if !RELEASE
        if (roomInfo is TestRoomInfo)
        {
            if (DateTime.UtcNow < roomInfo.EndRunningTime) return;

            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = await dbContext.TestUsers.SingleOrDefaultAsync(u => u.Id == gameResult.First.UserId);
            if (first == null) return;

            first.WinCount++;
            
            dbContext.TestUsers.Update(first);
            await dbContext.SaveChangesAsync();
            return;
        }
#endif
        
        var apiClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
        await apiClient.PostAsync(API_URL.Result, new StringContent(
            JsonSerializer.Serialize(gameResult),
            Encoding.UTF8,
            "application/json"));
    }
}