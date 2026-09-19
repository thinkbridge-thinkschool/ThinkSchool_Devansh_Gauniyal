using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Capstone.Invoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Status",
                schema: "invoicing",
                table: "Invoices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SubmittedAt",
                schema: "invoicing",
                table: "Invoices",
                column: "SubmittedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_Status",
                schema: "invoicing",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_SubmittedAt",
                schema: "invoicing",
                table: "Invoices");
        }
    }
}
