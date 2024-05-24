using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;
using Swashbuckle.AspNetCore.Annotations;
using Tori.Controllers.Requests;
using tori.Core;
using tori.Models;

namespace Tori.Controllers;

#if !RELEASE
[ApiController]
[Route("[controller]")]
public class TestController : Controller
{
    private readonly ILogger<TestController> logger;
    private readonly AppDbContext dbContext;

    public TestController(ILogger<TestController> logger, AppDbContext dbContext)
    {
        this.logger = logger;
        this.dbContext = dbContext;
    }

    private async Task<IActionResult> HandleExceptionAsync(IDbContextTransaction? transaction, Exception e, string message,
        params object[] args)
    {
        if (transaction != null) await transaction.RollbackAsync();
        
#pragma warning disable CA2254
        this.logger.LogCritical(e, message, args);
#pragma warning restore CA2254

        var detail = e switch
        {
            MySqlException => "SQL Exception",
            InvalidOperationException exception => exception.Message,
            _ => null
        };

        return this.Problem(detail ?? "Failed to process operation", statusCode: StatusCodes.Status500InternalServerError);
    }
    
    [HttpPost]
    [Route("room")]
    [SwaggerOperation("테스트용 방 생성", "테스트를 위한 방 정보를 생성합니다. 동일한 방 ID를 가진 테스트 방 정보가 이미 있을 경우 덮어씌웁니다.")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTestRoom([FromBody] CreateTestRoomBody req)
    {
        var begin = new DateTime(req.GameStartUtc);
        var end = new DateTime(req.GameEndUtc);
        var expire = new DateTime(req.ExpireAtUtc);
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            
            var duplicate = await this.dbContext.TestRooms.SingleOrDefaultAsync(t => t.RoomId == req.RoomId);

            if (duplicate == null)
            {
                await this.dbContext.TestRooms.AddAsync(new TestRoom
                {
                    RoomId = req.RoomId,
                    BeginRunningTime = begin,
                    EndRunningTime = end,
                    ExpireAt = expire
                });
            }
            else
            {
                duplicate.BeginRunningTime = begin;
                duplicate.EndRunningTime = end;
                duplicate.ExpireAt = expire;
                this.dbContext.TestRooms.Update(duplicate);
            }

            await this.dbContext.SaveChangesAsync();
            
            await transaction.CommitAsync();
            
            return this.Ok();
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - [test]create room [roomId : {roomId}, begin : {begin}, end : {end}]",
                req.RoomId, begin, end);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    [HttpDelete]
    [Route("room")]
    [SwaggerOperation("테스트용 방 삭제", "테스트를 위한 방 정보를 삭제합니다.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "해당 정보를 찾지 못했습니다.")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTestRoom([FromBody] DeleteTestRoomBody req)
    {
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            
            var duplicate = await this.dbContext.TestRooms.SingleOrDefaultAsync(t => t.RoomId == req.RoomId);

            if (duplicate == null)
            {
                await transaction.RollbackAsync();
                return this.NotFound();
            }
            
            this.dbContext.TestRooms.Remove(duplicate);
            await this.dbContext.SaveChangesAsync();
            
            await transaction.CommitAsync();
            
            return this.Ok();
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - [test]delete room [roomId : {roomId}]", req.RoomId);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    [HttpPost]
    [Route("user")]
    [SwaggerOperation("테스트용 유저 생성", "테스트를 위한 유저 정보를 생성합니다. 동일한 유저 ID를 가진 테스트 유저 정보가 이미 있을 경우 덮어씌웁니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTestUser([FromBody] CreateTestUserBody req)
    {
        if (!int.TryParse(req.UserId, out var userId)) return this.BadRequest();
        
        var expire = new DateTime(req.ExpireAtUtc);
        var inventoryJson =
            JsonSerializer.Serialize(req.Items.ToDictionary(it => int.Parse(it.Key), it => it.Value));
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var duplicate = await this.dbContext.TestUsers.SingleOrDefaultAsync(u => u.Id == userId);

            if (duplicate == null)
            {
                await this.dbContext.TestUsers.AddAsync(new TestUser
                {
                    Id = userId,
                    Nickname = req.UserNickname,
                    WinCount = req.WinnerCount,
                    InventoryJson = inventoryJson,
                    Energy = req.Energy,
                    ExpireAt = expire
                });
            }
            else
            {
                duplicate.Nickname = req.UserNickname;
                duplicate.WinCount = req.WinnerCount;
                duplicate.InventoryJson = inventoryJson;
                duplicate.Energy = req.Energy;
                duplicate.ExpireAt = expire;
                this.dbContext.TestUsers.Update(duplicate);
            }

            await this.dbContext.SaveChangesAsync();
            
            await transaction.CommitAsync();
            
            return this.Ok();
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - [test]create user [userId : {userId}, nickname : {userNickname}, winCount : {winCount}, inventory : {inventory}, energy : {energy}]",
                req.UserId, req.UserNickname, req.WinnerCount, inventoryJson, req.Energy);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }
    
    [HttpDelete]
    [Route("user")]
    [SwaggerOperation("테스트용 유저 삭제", "테스트를 위한 유저 정보를 삭제합니다.")]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "정상적이지 않은 값으로 API를 호출하여 처리에 실패했습니다.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "해당 정보를 찾지 못했습니다.")]
    [SwaggerResponse(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTestUser([FromBody] DeleteTestUserBody req)
    {
        if (!int.TryParse(req.UserId, out var userId)) return this.BadRequest();
        
        var transaction = await this.dbContext.Database.BeginTransactionAsync();
        try
        {
            var duplicate = await this.dbContext.TestUsers.SingleOrDefaultAsync(u => u.Id == userId);

            if (duplicate == null)
            {
                await transaction.RollbackAsync();
                return this.NotFound();
            }
            
            this.dbContext.TestUsers.Remove(duplicate);
            await this.dbContext.SaveChangesAsync();
            
            await transaction.CommitAsync();
            
            return this.Ok();
        }
        catch (Exception e)
        {
            return await this.HandleExceptionAsync(transaction, e,
                "API HAS EXCEPTION - [test]create user [userId : {userId}]", req.UserId);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }
}
#endif