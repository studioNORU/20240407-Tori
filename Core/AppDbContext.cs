using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using tori.Models;

namespace tori.Core;

public class AppDbContext : DbContext
{
    public DbSet<GameConstant> GameConstants { get; set; } = default!;
    public DbSet<GameStage> GameStages { get; set; } = default!;
    public DbSet<GameUser> GameUsers { get; set; } = default!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        Env.Load();
        
        var builder = new MySqlConnectionStringBuilder();
        builder.Server = Env.GetString("DB_HOST", "localhost");
        builder.Port = (uint)Env.GetInt("DB_PORT", 3306);
        builder.Database = Env.GetString("DB_NAME", "database");
        builder.UserID = Env.GetString("DB_USER", "mysql");
        builder.Password = Env.GetString("DB_PASSWORD", "password");

        var connection = new MySqlConnection(builder.ConnectionString);
        optionsBuilder.UseMySql(connection, ServerVersion.AutoDetect(connection));
    }
}