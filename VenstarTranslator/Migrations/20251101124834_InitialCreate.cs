using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VenstarTranslator.Migrations
{
    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sensors",
                columns: table => new
                {
                    SensorID = table.Column<byte>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<ushort>(type: "INTEGER", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    Scale = table.Column<string>(type: "TEXT", nullable: false),
                    URL = table.Column<string>(type: "TEXT", nullable: false),
                    IgnoreSSLErrors = table.Column<bool>(type: "INTEGER", nullable: false),
                    JSONPath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sensors", x => x.SensorID);
                });

            migrationBuilder.CreateTable(
                name: "DataSourceHttpHeader",
                columns: table => new
                {
                    ID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedVenstarSensorSensorID = table.Column<byte>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSourceHttpHeader", x => x.ID);
                    table.ForeignKey(
                        name: "FK_DataSourceHttpHeader_Sensors_TranslatedVenstarSensorSensorID",
                        column: x => x.TranslatedVenstarSensorSensorID,
                        principalTable: "Sensors",
                        principalColumn: "SensorID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataSourceHttpHeader_TranslatedVenstarSensorSensorID",
                table: "DataSourceHttpHeader",
                column: "TranslatedVenstarSensorSensorID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataSourceHttpHeader");

            migrationBuilder.DropTable(
                name: "Sensors");
        }
    }
}
