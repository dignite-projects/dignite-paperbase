using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dignite.Paperbase.Host.Migrations
{
    /// <summary>
    /// issue #188：drop 自造 <c>PaperbaseOutboxEvents</c>（never-true "in-flight 替换"实现），
    /// 改用 ABP 内置 transactional outbox（<c>AbpEventInbox</c> / <c>AbpEventOutbox</c>）。
    /// <para>
    /// <b>部署 runbook</b>：
    /// <list type="number">
    ///   <item>apply 前停 host 5–10s 让旧表的 outbox sender 循环排空（实际旧表无下游 ack，仅作仪式性等待）。</item>
    ///   <item>apply migration。</item>
    ///   <item>重启 host。ABP <c>OutboxSenderJob</c> / <c>InboxProcessorJob</c> 自动开始扫描新表。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>⚠️ 不可安全回滚</b>：<c>Down()</c> drop <c>AbpEventInbox</c> / <c>AbpEventOutbox</c> 会丢失
    /// 这两张表中所有未投递事件；同时重建的 <c>PaperbaseOutboxEvents</c> 为空表。
    /// 生产 rollback 必须走手动 DR 流程，而非 EF migration revert。
    /// </para>
    /// </summary>
    public partial class Drop_OutboxEvent_Use_AbpEventBoxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperbaseOutboxEvents");

            migrationBuilder.CreateTable(
                name: "AbpEventInbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EventData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HandledTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    NextRetryTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpEventInbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpEventOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EventName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EventData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpEventOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpEventInbox_MessageId",
                table: "AbpEventInbox",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpEventInbox_Status_CreationTime",
                table: "AbpEventInbox",
                columns: new[] { "Status", "CreationTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpEventOutbox_CreationTime",
                table: "AbpEventOutbox",
                column: "CreationTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 与上方 XML doc 一致：drop AbpEventInbox/Outbox 会丢失所有未投递事件，
            // 同时重建的 PaperbaseOutboxEvents 是空表——这是单向不可逆变更。
            // 把 runbook 升格为代码强制，避免误执行 `dotnet ef database update <prev>` 触发数据丢失。
            // 如需手动 DR，请直接执行下方 schema 重建 SQL（保留作 historical reference）。
            throw new NotSupportedException(
                "Drop_OutboxEvent_Use_AbpEventBoxes is intentionally non-reversible. " +
                "Reverting drops AbpEventInbox/AbpEventOutbox losing all in-flight events. " +
                "Use a manual DR procedure instead of `ef database update <previous>`. " +
                "See migration XML doc for runbook.");

#pragma warning disable CS0162 // Unreachable code — kept as historical reference for manual DR.
            migrationBuilder.DropTable(
                name: "AbpEventInbox");

            migrationBuilder.DropTable(
                name: "AbpEventOutbox");

            migrationBuilder.CreateTable(
                name: "PaperbaseOutboxEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperbaseOutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseOutboxEvents_Status_ScheduledAt",
                table: "PaperbaseOutboxEvents",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaperbaseOutboxEvents_TenantId_DocumentId_EventType",
                table: "PaperbaseOutboxEvents",
                columns: new[] { "TenantId", "DocumentId", "EventType" },
                unique: true,
                filter: "IsDeleted = 0");
#pragma warning restore CS0162
        }
    }
}
