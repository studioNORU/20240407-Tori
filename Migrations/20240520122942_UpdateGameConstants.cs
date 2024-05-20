using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace tori.Migrations
{
    /// <inheritdoc />
    public partial class UpdateGameConstants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "GameConstants",
                keyColumn: "Key",
                keyValue: "CONST_SYSTEM_STEAL_ITEM_RANGE");

            migrationBuilder.InsertData(
                table: "GameConstants",
                columns: new[] { "Key", "Value" },
                values: new object[,]
                {
                    { "CONST_SYSTEM_SMOKE_DURATION", 1500 },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "GameConstants",
                keyColumn: "Key",
                keyValue: "CONST_SYSTEM_SMOKE_DURATION");
            
            migrationBuilder.InsertData(
                table: "GameConstants",
                columns: new[] { "Key", "Value" },
                values: new object[,]
                {
                    { "CONST_SYSTEM_STEAL_ITEM_RANGE", 5000 },
                });
        }
    }
}
