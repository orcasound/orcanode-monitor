using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrcanodeMonitor.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioStandardDeviation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastOrcaHelloDetectionComments",
                table: "Orcanode");

            migrationBuilder.DropColumn(
                name: "LastOrcaHelloDetectionConfidence",
                table: "Orcanode");

            migrationBuilder.DropColumn(
                name: "LastOrcaHelloDetectionFound",
                table: "Orcanode");

            migrationBuilder.DropColumn(
                name: "LastOrcaHelloDetectionTimestamp",
                table: "Orcanode");

            migrationBuilder.AddColumn<double>(
                name: "AudioStandardDeviation",
                table: "Orcanode",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioStandardDeviation",
                table: "Orcanode");

            migrationBuilder.AddColumn<string>(
                name: "LastOrcaHelloDetectionComments",
                table: "Orcanode",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LastOrcaHelloDetectionConfidence",
                table: "Orcanode",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LastOrcaHelloDetectionFound",
                table: "Orcanode",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastOrcaHelloDetectionTimestamp",
                table: "Orcanode",
                type: "datetime2",
                nullable: true);
        }
    }
}
