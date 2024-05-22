using DotNetEnv;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Serilog;
using tori.AppApi;
using tori.Core;

Env.Load();

var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Add services to the container.
    builder.Host.UseSerilog();
    builder.Services.AddDbContext<AppDbContext>();
    builder.Services.AddSingleton<ApiClient>();
    builder.Services.AddHostedService<UserHealthCheckService>();
    builder.Services.AddHostedService<PostGameResultService>();
    builder.Services.AddControllers(config =>
    {
        foreach (var formatter in config.InputFormatters)
        {
            if (formatter is SystemTextJsonInputFormatter jsonInputFormatter)
            {
                jsonInputFormatter.SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/plain"));
            }
        }
    });
    builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Version = "v1",
            Title = "토리를 구해줘",
            Description = "'토리를 구해줘' 게임에 대한 API 문서입니다. (본 API에서의 시간 정보는 모두 UTC를 기준으로 하고 있습니다.)"
        });

        options.EnableAnnotations();
    });

    var app = builder.Build();

#if RELEASE && !DEV
    if (app.Environment.IsDevelopment())
#endif
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    app.UseRouting();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception e)
{
    Log.Fatal(e, "Terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
