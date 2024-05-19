namespace tori.Core;

public class UserHealthCheckService : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Constants.UserHealthCheckIntervalSeconds);
    private readonly TimeSpan threshold = TimeSpan.FromSeconds(Constants.UserHealthCheckThresholdSeconds);
    private readonly ILogger<UserHealthCheckService> logger;

    public UserHealthCheckService(ILogger<UserHealthCheckService> logger)
    {
        this.logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var disconnected = SessionManager.I.DisconnectInactiveUsers(this.threshold);
            this.logger.LogInformation("Disconnected {count} inactive users", disconnected);
            await Task.Delay(this.interval, stoppingToken);
        }
    }
}