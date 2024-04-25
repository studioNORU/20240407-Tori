using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace tori.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        private static readonly string[] columns = new[] { "Key", "Value" };
        private static readonly object[,] values = new object[,]
        {
            { "CONST_SYSTEM_MOVE_SPEED", 150000 },
            { "CONST_SYSTEM_MOVE_SPEED_BOOST", 300000 },
            { "CONST_SYSTEM_ENERGY_MAX", 100 },
            { "CONST_SYSTEM_DECREASE_ENERGY_PER_MINUTE", 10 },
            { "CONST_SYSTEM_STEAL_RANGE", 1500 },
            { "CONST_SYSTEM_STEAL_ITEM_RANGE", 5000 },
            { "CONST_SYSTEM_SHIELD_DURATION_HOST", 4000 },
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GameConstants",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameConstants", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "GameConstants",
                columns: columns,
                values: values);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameConstants");
        }
    }
}
