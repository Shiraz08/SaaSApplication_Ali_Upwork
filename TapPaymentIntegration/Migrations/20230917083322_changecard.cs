using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TapPaymentIntegration.Migrations
{
    public partial class changecard : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "changeCardInfos",
                columns: table => new
                {
                    ChangeCardId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OldCardName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewCardName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubscriptionName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeCardDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_changeCardInfos", x => x.ChangeCardId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "changeCardInfos");
        }
    }
}
