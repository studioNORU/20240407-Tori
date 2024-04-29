using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace tori.Migrations
{
    /// <inheritdoc />
    public partial class AddGameStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameStages",
                columns: table => new
                {
                    StageId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaxPlayer = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<int>(type: "int", nullable: false),
                    AiPoolId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameStages", x => x.StageId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "GameStages",
                columns: new string[] { "StageId", "MaxPlayer", "Time", "AiPoolId" },
                values: new object[,]
                {
                    { "Stage001", 50, 180000, "AIPool001" },
                    { "Stage002", 50, 180000, "AIPool002" },
                    { "Stage003", 50, 180000, "AIPool003" },
                    { "Stage004", 50, 180000, "AIPool004" },
                    { "Stage005", 50, 180000, "AIPool005" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameStages");
        }
    }
}
