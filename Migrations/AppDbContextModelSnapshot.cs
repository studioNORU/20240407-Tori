﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using tori.Core;

#nullable disable

namespace tori.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.4")
                .HasAnnotation("Relational:MaxIdentifierLength", 64);

            MySqlModelBuilderExtensions.AutoIncrementColumns(modelBuilder);

            modelBuilder.Entity("tori.Models.GameConstant", b =>
                {
                    b.Property<string>("Key")
                        .HasColumnType("varchar(255)");

                    b.Property<int>("Value")
                        .HasColumnType("int");

                    b.HasKey("Key");

                    b.ToTable("GameConstants");
                });

            modelBuilder.Entity("tori.Models.GamePlayData", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    MySqlPropertyBuilderExtensions.UseMySqlIdentityColumn(b.Property<int>("Id"));

                    b.Property<int>("RoomId")
                        .HasColumnType("int");

                    b.Property<DateTime>("TimeStamp")
                        .HasColumnType("datetime(6)");

                    b.Property<string>("UseItems")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("RoomId", "UserId")
                        .IsUnique();

                    b.ToTable("PlayData");
                });

            modelBuilder.Entity("tori.Models.GameStage", b =>
                {
                    b.Property<string>("StageId")
                        .HasColumnType("varchar(255)");

                    b.Property<string>("AiPoolId")
                        .IsRequired()
                        .HasColumnType("longtext");

                    b.Property<int>("MaxPlayer")
                        .HasColumnType("int");

                    b.Property<int>("Time")
                        .HasColumnType("int");

                    b.HasKey("StageId");

                    b.ToTable("GameStages");
                });

            modelBuilder.Entity("tori.Models.GameUser", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    MySqlPropertyBuilderExtensions.UseMySqlIdentityColumn(b.Property<int>("Id"));

                    b.Property<DateTime?>("JoinedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<DateTime?>("LeavedAt")
                        .HasColumnType("datetime(6)");

                    b.Property<int>("RoomId")
                        .HasColumnType("int");

                    b.Property<int>("Status")
                        .HasColumnType("int");

                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("RoomId", "UserId");

                    b.ToTable("GameUsers");
                });

            modelBuilder.Entity("tori.Models.GamePlayData", b =>
                {
                    b.HasOne("tori.Models.GameUser", "GameUser")
                        .WithOne("PlayData")
                        .HasForeignKey("tori.Models.GamePlayData", "RoomId", "UserId")
                        .HasPrincipalKey("tori.Models.GameUser", "RoomId", "UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("GameUser");
                });

            modelBuilder.Entity("tori.Models.GameUser", b =>
                {
                    b.Navigation("PlayData");
                });
#pragma warning restore 612, 618
        }
    }
}
