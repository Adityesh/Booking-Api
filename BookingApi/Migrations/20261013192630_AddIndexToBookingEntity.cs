using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexToBookingEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_bookings_start_time_confirmed",
                table: "bookings",
                column: "start_time",
                filter: "status = 'Confirmed'")
                .Annotation("Npgsql:IndexInclude", new[] { "resource_id", "end_time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_start_time_confirmed",
                table: "bookings");
        }
    }
}
