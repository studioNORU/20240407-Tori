using tori.AppApi;

namespace tori.Core;

public class UserHealthCheckService : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Constants.UserHealthCheckIntervalSeconds);
    private readonly TimeSpan threshold = TimeSpan.FromSeconds(Constants.UserHealthCheckThresholdSeconds);
    private readonly ILogger<UserHealthCheckService> logger;
    private readonly IServiceScopeFactory serviceScopeFactory;

    public UserHealthCheckService(ILogger<UserHealthCheckService> logger, IServiceScopeFactory serviceScopeFactory)
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
                var disconnected =
                    SessionManager.I.DisconnectInactiveUsers(apiClient, dbContext, this.threshold);
                if (0 < disconnected) this.logger.LogInformation("Disconnected {count} inactive users", disconnected);
                await transaction.CommitAsync(stoppingToken);
                await Task.Delay(this.interval, stoppingToken);
            }
            catch (Exception e)
            {
                await transaction.RollbackAsync(stoppingToken);
                this.logger.LogCritical(e, "HAS EXCEPTION ON USER HEALTH CHECK SERVICE");
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }
    }
}