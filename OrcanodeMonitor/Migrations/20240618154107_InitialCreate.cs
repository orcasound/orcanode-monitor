using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrcanodeMonitor.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
#if false
            migrationBuilder.CreateTable(
                name: "MonitorState",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedTimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitorState", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "Orcanode",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DataplicitySerial = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrcasoundName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    S3NodeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    S3Bucket = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrcasoundSlug = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LatestRecordedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LatestUploadedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManifestUpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastCheckedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataplicityName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataplicityDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AgentVersion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DiskCapacity = table.Column<long>(type: "bigint", nullable: false),
                    DiskUsed = table.Column<long>(type: "bigint", nullable: false),
                    DataplicityOnline = table.Column<bool>(type: "bit", nullable: true),
                    DataplicityUpgradeAvailable = table.Column<bool>(type: "bit", nullable: true),
                    LastOrcaHelloDetectionTimestamp = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastOrcaHelloDetectionConfidence = table.Column<int>(type: "int", nullable: true),
                    LastOrcaHelloDetectionComments = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastOrcaHelloDetectionFound = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orcanode", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "OrcanodeEvent",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Slug = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrcanodeId = table.Column<int>(type: "int", nullable: false),
                    DateTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrcanodeEvent", x => x.ID);
                    table.ForeignKey(
                        name: "FK_OrcanodeEvent_Orcanode_OrcanodeId",
                        column: x => x.OrcanodeId,
                        principalTable: "Orcanode",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrcanodeEvent_OrcanodeId",
                table: "OrcanodeEvent",
                column: "OrcanodeId");
#endif
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonitorState");

            migrationBuilder.DropTable(
                name: "OrcanodeEvent");

            migrationBuilder.DropTable(
                name: "Orcanode");
        }
    }
}
