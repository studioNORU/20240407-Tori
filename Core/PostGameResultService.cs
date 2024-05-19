﻿using tori.AppApi;

namespace tori.Core;

public class PostGameResultService : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Constants.PostGameResultIntervalSeconds);
    private readonly ILogger<PostGameResultService> logger;
    private readonly IServiceScopeFactory serviceScopeFactory;

    public PostGameResultService(ILogger<PostGameResultService> logger, IServiceScopeFactory serviceScopeFactory)
    {
        this.logger = logger;
        this.serviceScopeFactory = serviceScopeFactory;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = this.serviceScopeFactory.CreateScope();
            var apiClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transaction = await dbContext.Database.BeginTransactionAsync(stoppingToken);
            try
            {
                await SessionManager.I.PostGameResult(apiClient, dbContext);
                await transaction.CommitAsync(stoppingToken);
                await Task.Delay(this.interval, stoppingToken);
            }
            catch (Exception e)
            {
                await transaction.RollbackAsync(stoppingToken);
                this.logger.LogCritical(e, "EXCEPTION ON POST GAME RESULT SERVICE");
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }
    }
}