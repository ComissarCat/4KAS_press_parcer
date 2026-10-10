using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Press.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsOwn = table.Column<bool>(type: "boolean", nullable: false),
                    IsRepost = table.Column<bool>(type: "boolean", nullable: false),
                    TitleIsManual = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SearchKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Login = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceMaterialId = table.Column<int>(type: "integer", nullable: false),
                    DerivedMaterialId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: true),
                    Note = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialRelations_Materials_DerivedMaterialId",
                        column: x => x.DerivedMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterialRelations_Materials_SourceMaterialId",
                        column: x => x.SourceMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaterialTypeAssignments",
                columns: table => new
                {
                    MaterialsId = table.Column<int>(type: "integer", nullable: false),
                    TypesId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialTypeAssignments", x => new { x.MaterialsId, x.TypesId });
                    table.ForeignKey(
                        name: "FK_MaterialTypeAssignments_MaterialTypes_TypesId",
                        column: x => x.TypesId,
                        principalTable: "MaterialTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterialTypeAssignments_Materials_MaterialsId",
                        column: x => x.MaterialsId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Resources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    IsOwn = table.Column<bool>(type: "boolean", nullable: false),
                    HomeUrl = table.Column<string>(type: "text", nullable: true),
                    UrlPrefix = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CollectorKind = table.Column<int>(type: "integer", nullable: false),
                    CollectorTarget = table.Column<string>(type: "text", nullable: true),
                    CollectorSelector = table.Column<string>(type: "text", nullable: true),
                    FetchFullText = table.Column<bool>(type: "boolean", nullable: false),
                    IsCollectorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CollectIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    DefaultMaterialTypeId = table.Column<int>(type: "integer", nullable: true),
                    LastCollectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCollectSucceededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCollectMessage = table.Column<string>(type: "text", nullable: true),
                    LastCollectFailed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Resources_MaterialTypes_DefaultMaterialTypeId",
                        column: x => x.DefaultMaterialTypeId,
                        principalTable: "MaterialTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MaterialLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaterialId = table.Column<int>(type: "integer", nullable: false),
                    ResourceId = table.Column<int>(type: "integer", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: false),
                    UrlKey = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OutLinkKeys = table.Column<string>(type: "text", nullable: true),
                    IsAutoCollected = table.Column<bool>(type: "boolean", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialLinks_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterialLinks_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MonitoredItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ResourceId = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    UrlKey = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FoundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SuggestedMaterialId = table.Column<int>(type: "integer", nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: true),
                    MatchInfo = table.Column<string>(type: "text", nullable: true),
                    DerivedMaterialId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitoredItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitoredItems_Materials_DerivedMaterialId",
                        column: x => x.DerivedMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MonitoredItems_Materials_SuggestedMaterialId",
                        column: x => x.SuggestedMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MonitoredItems_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLinks_MaterialId",
                table: "MaterialLinks",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLinks_ResourceId",
                table: "MaterialLinks",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLinks_UrlKey",
                table: "MaterialLinks",
                column: "UrlKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRelations_DerivedMaterialId",
                table: "MaterialRelations",
                column: "DerivedMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRelations_SourceMaterialId_DerivedMaterialId",
                table: "MaterialRelations",
                columns: new[] { "SourceMaterialId", "DerivedMaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialRelations_Status",
                table: "MaterialRelations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_IsOwn",
                table: "Materials",
                column: "IsOwn");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_PublishedAt",
                table: "Materials",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialTypeAssignments_TypesId",
                table: "MaterialTypeAssignments",
                column: "TypesId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialTypes_Name",
                table: "MaterialTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredItems_DerivedMaterialId",
                table: "MonitoredItems",
                column: "DerivedMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredItems_ResourceId",
                table: "MonitoredItems",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredItems_Status_PublishedAt",
                table: "MonitoredItems",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredItems_SuggestedMaterialId",
                table: "MonitoredItems",
                column: "SuggestedMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredItems_UrlKey",
                table: "MonitoredItems",
                column: "UrlKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Resources_DefaultMaterialTypeId",
                table: "Resources",
                column: "DefaultMaterialTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Login",
                table: "Users",
                column: "Login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "MaterialLinks");

            migrationBuilder.DropTable(
                name: "MaterialRelations");

            migrationBuilder.DropTable(
                name: "MaterialTypeAssignments");

            migrationBuilder.DropTable(
                name: "MonitoredItems");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Materials");

            migrationBuilder.DropTable(
                name: "Resources");

            migrationBuilder.DropTable(
                name: "MaterialTypes");
        }
    }
}
