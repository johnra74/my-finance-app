using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyFinance.Data.Migrations
{
    /// <inheritdoc />
    public partial class DecodeMoneyNumberField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data only: nothing about the shape of the book changes. See MoneyNumberRepair
            // for the encoding this undoes and for why the rules are as narrow as they are.
            migrationBuilder.Sql(MoneyNumberRepair.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo. The flag is not recoverable from the decoded value -- a bare
            // "1168" could have been padded to any width -- and nothing downstream wants it
            // back. Going down leaves the numbers readable, which is no worse than before.
        }
    }
}
