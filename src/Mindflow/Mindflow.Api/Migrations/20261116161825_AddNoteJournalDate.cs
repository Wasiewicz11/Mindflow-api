using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mindflow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteJournalDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "journal_date",
                table: "notes",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notes_user_id_journal_date",
                table: "notes",
                columns: new[] { "user_id", "journal_date" },
                unique: true,
                filter: "journal_date IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notes_user_id_journal_date",
                table: "notes");

            migrationBuilder.DropColumn(
                name: "journal_date",
                table: "notes");
        }
    }
}
