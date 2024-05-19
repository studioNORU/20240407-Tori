using tori.AppApi;

namespace tori.Core;

public class UserHealthCheckService : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Constants.UserHealthCheckIntervalSeconds);
    private readonly TimeSpan threshold = TimeSpan.FromSeconds(Constants.UserHealthCheckThresholdSeconds);
    private readonly ILogger<UserHealthCheckService> logger;
    private readonly AppDbContext dbContext;
    private readonly ApiClient apiClient;

    public UserHealthCheckService(ILogger<UserHealthCheckService> logger, AppDbContext dbContext, ApiClient apiClient)
    {
        this.logger = logger;
        this.dbContext = dbContext;
        this.apiClient = apiClient;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var transaction = await this.dbContext.Database.BeginTransactionAsync(stoppingToken);
            try
            {
                var disconnected =
                    SessionManager.I.DisconnectInactiveUsers(this.apiClient, this.dbContext, this.threshold);
                this.logger.LogInformation("Disconnected {count} inactive users", disconnected);
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