using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class PersistentPhotoAlbums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pirate_persistent_photo_albums",
                columns: table => new
                {
                    pirate_persistent_photo_albums_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    album_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    saved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pirate_persistent_photo_albums", x => x.pirate_persistent_photo_albums_id);
                });

            migrationBuilder.CreateTable(
                name: "pirate_persistent_photo_album_photos",
                columns: table => new
                {
                    pirate_persistent_photo_album_photos_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    album_id = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    image_data = table.Column<byte[]>(type: "bytea", nullable: false),
                    preview_data = table.Column<byte[]>(type: "bytea", nullable: true),
                    custom_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    custom_description = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    caption = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    base_description = table.Column<string>(type: "text", nullable: true),
                    capture_data_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pirate_persistent_photo_album_photos", x => x.pirate_persistent_photo_album_photos_id);
                    table.ForeignKey(
                        name: "FK_pirate_persistent_photo_album_photos_pirate_persistent_phot~",
                        column: x => x.album_id,
                        principalTable: "pirate_persistent_photo_albums",
                        principalColumn: "pirate_persistent_photo_albums_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pirate_persistent_photo_album_photos_album_id_sort_order",
                table: "pirate_persistent_photo_album_photos",
                columns: new[] { "album_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pirate_persistent_photo_albums_owner_kind_owner_id_album_key",
                table: "pirate_persistent_photo_albums",
                columns: new[] { "owner_kind", "owner_id", "album_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pirate_persistent_photo_album_photos");

            migrationBuilder.DropTable(
                name: "pirate_persistent_photo_albums");
        }
    }
}
