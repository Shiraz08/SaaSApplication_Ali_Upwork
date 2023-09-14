using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TapPaymentIntegration.Migrations
{
    public partial class subid : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Tap_Token_ID",
                table: "AspNetUsers",
                newName: "Tap_Subscription_ID");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Tap_Subscription_ID",
                table: "AspNetUsers",
                newName: "Tap_Token_ID");
        }
    }
}
