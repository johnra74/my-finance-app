using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyFinance.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccountOfxLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OfxAccountKey",
                table: "Accounts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfxBankId",
                table: "Accounts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_OfxAccountKey",
                table: "Accounts",
                column: "OfxAccountKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Accounts_OfxAccountKey",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "OfxAccountKey",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "OfxBankId",
                table: "Accounts");
        }
    }
}
