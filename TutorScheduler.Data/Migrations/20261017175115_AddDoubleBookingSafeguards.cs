using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TutorScheduler.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDoubleBookingSafeguards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_LessonSlotId",
                table: "Bookings");

            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleVersion",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_LessonSlotId_ActiveUnique",
                table: "Bookings",
                column: "LessonSlotId",
                unique: true,
                filter: "\"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_LessonSlotId_ActiveUnique",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ScheduleVersion",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_LessonSlotId",
                table: "Bookings",
                column: "LessonSlotId");
        }
    }
}
