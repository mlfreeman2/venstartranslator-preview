using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VenstarTranslator.Migrations
{
    /// <inheritdoc />
    public partial class AddLastErrorMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastErrorMessage",
                table: "Sensors",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastErrorMessage",
                table: "Sensors");
        }
    }
}
