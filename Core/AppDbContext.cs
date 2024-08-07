﻿using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using tori.Models;

namespace tori.Core;

public class AppDbContext : DbContext
{
    public DbSet<GameConstant> GameConstants { get; set; } = default!;
    public DbSet<GameStage> GameStages { get; set; } = default!;
    public DbSet<GameUser> GameUsers { get; set; } = default!;
    public DbSet<GamePlayData> PlayData { get; set; } = default!;
    public DbSet<GameLog> Logs { get; set; } = default!;
    
#if DEBUG || DEV
    public DbSet<TestRoom> TestRooms { get; set; } = default!;
    public DbSet<TestUser> TestUsers { get; set; } = default!;
#endif

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameUser>()
            .HasOne(e => e.PlayData)
            .WithOne(e => e.GameUser)
            .HasForeignKey<GamePlayData>(e => new { e.RoomId, e.UserId })
            .HasPrincipalKey<GameUser>(e => new { e.RoomId, e.UserId });
    }
}