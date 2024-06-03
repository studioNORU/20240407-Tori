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
    private readonly ILogger<DataFetcher> logger;
    private readonly IServiceProvider serviceProvider;

    public DataFetcher(ILogger<DataFetcher> logger, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.serviceProvider = serviceProvider;
    }

    public async Task<RoomInfo?> GetRoomInfo(int roomId)
    {
        await using var scope = this.serviceProvider.CreateAsyncScope();
#if DEBUG || DEV
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var test = await dbContext.TestRooms.SingleOrDefaultAsync(r => r.RoomId == roomId);
        var now = DateTime.UtcNow;
        this.logger.LogInformation("GET TEST ROOM - [roomId : {roomId}, expireAt : {expireAt}, now : {now}]", roomId, test?.ExpireAt.ToString() ?? "(null)", now);
        if (test != null && now < test.ExpireAt) return test.ToRoomInfo();
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
    #if DEBUG || DEV
        var userId = int.Parse(userIdStr);
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var test = await dbContext.TestUsers.SingleOrDefaultAsync(u => u.Id == userId);
        var now = DateTime.UtcNow;
        this.logger.LogInformation("GET TEST USER - [userId : {userId}, expireAt : {expireAt}, now : {now}]", userIdStr, test?.ExpireAt.ToString() ?? "(null)", now);
        if (test != null && now < test.ExpireAt) return test.ToUserInfo();
    #endif
        
        var apiClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
        return await apiClient.GetAsync<UserInfo>(API_URL.UserInfo, new Dictionary<string, string>
        {
            { "userNo", userIdStr },
        });
    }

#if DEBUG || DEV
    private async Task UpdateTestUser(int userId, int spentEnergy, UserStatus delta)
    {
        await using var scope = this.serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        var test = await dbContext.TestUsers.SingleOrDefaultAsync(u => u.Id == userId);
        if (test == null || test.ExpireAt < DateTime.UtcNow) return;
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
        this.logger.LogInformation("[UpdateTestUser] Updated test user [userId : {userId}, energy : {energy}, inventory : {inventory}]", userId, test.Energy, test.InventoryJson);
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

#if DEBUG || DEV
        if (user.UserInfo is TestUserInfo)
        {
            await this.UpdateTestUser(user.UserId, spentEnergy, delta);
            return;
        }
#endif

        var json = JsonSerializer.Serialize(delta);
        await apiClient.PostAsync(API_URL.UserStatus, new StringContent(
            json,
            Encoding.UTF8,
            "application/json"));
        this.logger.LogInformation("[UpdateUserStatus] Sent UserStatus request [userId : {userId}, json : {json}]", user.UserId, json);
    }

    public async Task SendResult(RoomInfo roomInfo, GameResult gameResult)
    {
        await using var scope = this.serviceProvider.CreateAsyncScope();
        var json = JsonSerializer.Serialize(gameResult);
        this.logger.LogInformation("[SendResult] Send game result requested [roomId : {roomId}, json : {json}]", roomInfo.RoomId, json);
        
#if DEBUG || DEV
        if (roomInfo is TestRoomInfo)
        {
            var now = DateTime.UtcNow;
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var test = await dbContext.TestRooms.SingleOrDefaultAsync(r => r.RoomId == roomInfo.RoomId);
            if (test == null || test.ExpireAt < now)
            {
                this.logger.LogInformation("[SendResult] TestRoom not found [roomId : {roomId}]", roomInfo.RoomId);
                return;
            }
            if (now < test.EndRunningTime)
            {
                this.logger.LogInformation("[SendResult] TestRoom still playing [roomId : {roomId}]", roomInfo.RoomId);
                return;
            }

            var first = await dbContext.TestUsers.SingleOrDefaultAsync(u => u.Id == gameResult.First.UserId);
            if (first == null)
            {
                this.logger.LogInformation("[SendResult] TestUser not found [roomId : {roomId}, winnerId : {userId}]", roomInfo.RoomId, gameResult.First.UserId);
                return;
            }

            first.WinCount++;
            
            dbContext.TestUsers.Update(first);
            await dbContext.SaveChangesAsync();
            this.logger.LogInformation("[SendResult] TestRoom processed game result successfully");
            return;
        }
#endif
        
        var apiClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
        await apiClient.PostAsync(API_URL.Result, new StringContent(
            json,
            Encoding.UTF8,
            "application/json"));
        this.logger.LogInformation("[SendResult] Sent Result request [roomId : {roomId}, json : {json}]", roomInfo.RoomId, json);
    }
}