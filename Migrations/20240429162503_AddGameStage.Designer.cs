﻿// <auto-generated />
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using tori.Core;

#nullable disable

namespace tori.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20240429162503_AddGameStage")]
    partial class AddGameStage
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
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
#pragma warning restore 612, 618
        }
    }
}