using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGateway.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewsIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Intro = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ResearchMode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ResearchSearchPasses = table.Column<int>(type: "INTEGER", nullable: true),
                    ResearchCandidates = table.Column<int>(type: "INTEGER", nullable: true),
                    ResearchDuplicatesRemoved = table.Column<int>(type: "INTEGER", nullable: true),
                    ResearchWindowHours = table.Column<int>(type: "INTEGER", nullable: true),
                    Document = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DayPostId = table.Column<long>(type: "INTEGER", nullable: true),
                    DayUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DayRevision = table.Column<int>(type: "INTEGER", nullable: true),
                    PublishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NoticeDeliveryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NoticeRevision = table.Column<int>(type: "INTEGER", nullable: true),
                    DeleteRequestedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NewsItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IssueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsTop = table.Column<bool>(type: "INTEGER", nullable: false),
                    Importance = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    WhyItMatters = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    PublishedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EventOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ReleaseStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    EventKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ImageAlt = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    PublishedRevision = table.Column<int>(type: "INTEGER", nullable: true),
                    WpPostId = table.Column<long>(type: "INTEGER", nullable: true),
                    WpUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ImageResult = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    ImageError = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsItems_NewsIssues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "NewsIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewsReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IssueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsReleases_NewsIssues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "NewsIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewsRemovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IssueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsRemovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsRemovals_NewsIssues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "NewsIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewsSecurityAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IssueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    AlertId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Product = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Cvss = table.Column<double>(type: "REAL", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsSecurityAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsSecurityAlerts_NewsIssues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "NewsIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewsSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsSources_NewsItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "NewsItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsIssues_Date",
                table: "NewsIssues",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_CanonicalUrl",
                table: "NewsItems",
                column: "CanonicalUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_EventKey",
                table: "NewsItems",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_IssueId_Number",
                table: "NewsItems",
                columns: new[] { "IssueId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsReleases_CanonicalUrl",
                table: "NewsReleases",
                column: "CanonicalUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsReleases_IssueId",
                table: "NewsReleases",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsRemovals_IssueId_EventKey",
                table: "NewsRemovals",
                columns: new[] { "IssueId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsSecurityAlerts_CanonicalUrl",
                table: "NewsSecurityAlerts",
                column: "CanonicalUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsSecurityAlerts_IssueId",
                table: "NewsSecurityAlerts",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsSources_CanonicalUrl",
                table: "NewsSources",
                column: "CanonicalUrl");

            migrationBuilder.CreateIndex(
                name: "IX_NewsSources_ItemId",
                table: "NewsSources",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewsReleases");

            migrationBuilder.DropTable(
                name: "NewsRemovals");

            migrationBuilder.DropTable(
                name: "NewsSecurityAlerts");

            migrationBuilder.DropTable(
                name: "NewsSources");

            migrationBuilder.DropTable(
                name: "NewsItems");

            migrationBuilder.DropTable(
                name: "NewsIssues");
        }
    }
}
