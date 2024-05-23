using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace tori.Migrations
{
    /// <inheritdoc />
    public partial class UpdateGameConstants_RemoveSmokeDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "GameConstants",
                keyColumn: "Key",
                keyValue: "CONST_SYSTEM_SMOKE_DURATION");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "GameConstants",
                columns: new[] { "Key", "Value" },
                values: new object[,]
                {
                    { "CONST_SYSTEM_SMOKE_DURATION", 1500 },
                });
        }
    }
}
