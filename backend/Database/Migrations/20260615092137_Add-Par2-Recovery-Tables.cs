using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPar2RecoveryTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Par2RecoverySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DirectoryDavItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecoverySetId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    SliceSize = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalRecoveryBlocks = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Par2RecoverySets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Par2RecoveryVolumes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecoverySetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlockCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SegmentIds = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Par2RecoveryVolumes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Par2SourceFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecoverySetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DavItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileLength = table.Column<long>(type: "INTEGER", nullable: false),
                    SliceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSliceIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Par2SourceFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Par2RecoverySets_DirectoryDavItemId",
                table: "Par2RecoverySets",
                column: "DirectoryDavItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Par2RecoveryVolumes_RecoverySetId",
                table: "Par2RecoveryVolumes",
                column: "RecoverySetId");

            migrationBuilder.CreateIndex(
                name: "IX_Par2SourceFiles_DavItemId",
                table: "Par2SourceFiles",
                column: "DavItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Par2SourceFiles_RecoverySetId",
                table: "Par2SourceFiles",
                column: "RecoverySetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Par2RecoverySets");

            migrationBuilder.DropTable(
                name: "Par2RecoveryVolumes");

            migrationBuilder.DropTable(
                name: "Par2SourceFiles");
        }
    }
}
