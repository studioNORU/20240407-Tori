using tori.AppApi;

namespace tori.Core;

public class PostGameResultService : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Constants.PostGameResultIntervalSeconds);
    private readonly ILogger<PostGameResultService> logger;
    private readonly ApiClient apiClient;
    private readonly AppDbContext dbContext;

    public PostGameResultService(ILogger<PostGameResultService> logger, ApiClient apiClient, AppDbContext dbContext)
    {
        this.logger = logger;
        this.apiClient = apiClient;
        this.dbContext = dbContext;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var transaction = await this.dbContext.Database.BeginTransactionAsync(stoppingToken);
            try
            {
                await SessionManager.I.PostGameResult(this.apiClient, this.dbContext);
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