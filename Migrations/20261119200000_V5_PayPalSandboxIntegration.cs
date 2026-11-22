using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PickleballClub.Models;

#nullable disable

namespace PickleballClub.Migrations;

[DbContext(typeof(PickleballContext))]
[Migration("20261119200000_V5_PayPalSandboxIntegration")]
public partial class V5_PayPalSandboxIntegration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID('tb_PayPalTransaction', 'U') IS NULL
BEGIN
    CREATE TABLE tb_PayPalTransaction (
        PayPalTransactionId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PublicAttemptCode NVARCHAR(30) NOT NULL,
        TargetType NVARCHAR(20) NOT NULL,
        BookingId INT NULL,
        OrderId INT NULL,
        TargetCode NVARCHAR(40) NOT NULL,
        PayPalOrderId NVARCHAR(64) NULL,
        PayPalCaptureId NVARCHAR(64) NULL,
        PayPalRefundId NVARCHAR(64) NULL,
        PayPalPayerId NVARCHAR(64) NULL,
        PayPalPayerEmail NVARCHAR(150) NULL,
        PayPalStatus NVARCHAR(32) NULL,
        LocalStatus NVARCHAR(40) NOT NULL CONSTRAINT DF_tb_PayPalTransaction_LocalStatus DEFAULT ('Creating'),
        AmountVnd INT NOT NULL,
        AmountVndDec DECIMAL(18, 2) NOT NULL CONSTRAINT DF_tb_PayPalTransaction_AmountVndDec DEFAULT (0),
        ExchangeRateVndPerUsd DECIMAL(18, 4) NOT NULL,
        AmountUsd DECIMAL(18, 2) NOT NULL,
        CurrencyCode NVARCHAR(10) NOT NULL CONSTRAINT DF_tb_PayPalTransaction_CurrencyCode DEFAULT ('USD'),
        RateSnapshotUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_PayPalTransaction_RateSnapshotUtc DEFAULT (SYSUTCDATETIME()),
        CreateIdempotencyKey NVARCHAR(100) NOT NULL,
        CaptureIdempotencyKey NVARCHAR(100) NULL,
        RefundIdempotencyKey NVARCHAR(100) NULL,
        LedgerTransactionId INT NULL,
        CompensationRefundLedgerTxId INT NULL,
        ReturnNonce NVARCHAR(64) NOT NULL,
        ApprovalUrl NVARCHAR(500) NULL,
        ActorAccountId INT NULL,
        CreatedBy NVARCHAR(100) NULL,
        ClientIpHash NVARCHAR(64) NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_PayPalTransaction_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_PayPalTransaction_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CapturedAtUtc DATETIME2 NULL,
        RefundedAtUtc DATETIME2 NULL,
        ExpiresAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_PayPalTransaction_ExpiresAtUtc DEFAULT (DATEADD(HOUR, 2, SYSUTCDATETIME())),
        LastErrorCode NVARCHAR(60) NULL,
        LastErrorMessage NVARCHAR(300) NULL,
        SanitizedSummaryJson NVARCHAR(MAX) NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT FK_tb_PayPalTransaction_tb_Booking FOREIGN KEY (BookingId) REFERENCES tb_Booking(BookingId),
        CONSTRAINT FK_tb_PayPalTransaction_tb_Order FOREIGN KEY (OrderId) REFERENCES tb_Order(OrderId),
        CONSTRAINT FK_tb_PayPalTransaction_tb_PaymentTransaction FOREIGN KEY (LedgerTransactionId) REFERENCES tb_PaymentTransaction(TransactionId),
        CONSTRAINT FK_tb_PayPalTransaction_RefundLedger FOREIGN KEY (CompensationRefundLedgerTxId) REFERENCES tb_PaymentTransaction(TransactionId)
    );
END
");

        migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_PayPalTransaction_AttemptCode' AND object_id = OBJECT_ID('tb_PayPalTransaction'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_PayPalTransaction_AttemptCode ON tb_PayPalTransaction(PublicAttemptCode);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_PayPalTransaction_CreateIdem' AND object_id = OBJECT_ID('tb_PayPalTransaction'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_PayPalTransaction_CreateIdem ON tb_PayPalTransaction(CreateIdempotencyKey);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_PayPalTransaction_OrderId' AND object_id = OBJECT_ID('tb_PayPalTransaction'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_PayPalTransaction_OrderId ON tb_PayPalTransaction(PayPalOrderId) WHERE [PayPalOrderId] IS NOT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_PayPalTransaction_CaptureId' AND object_id = OBJECT_ID('tb_PayPalTransaction'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_PayPalTransaction_CaptureId ON tb_PayPalTransaction(PayPalCaptureId) WHERE [PayPalCaptureId] IS NOT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_PayPalTransaction_RefundId' AND object_id = OBJECT_ID('tb_PayPalTransaction'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_PayPalTransaction_RefundId ON tb_PayPalTransaction(PayPalRefundId) WHERE [PayPalRefundId] IS NOT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_PayPalTransaction_LedgerTxId' AND object_id = OBJECT_ID('tb_PayPalTransaction'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_PayPalTransaction_LedgerTxId ON tb_PayPalTransaction(LedgerTransactionId) WHERE [LedgerTransactionId] IS NOT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_PayPalTransaction_CompRefundTxId' AND object_id = OBJECT_ID('tb_PayPalTransaction'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_PayPalTransaction_CompRefundTxId ON tb_PayPalTransaction(CompensationRefundLedgerTxId) WHERE [CompensationRefundLedgerTxId] IS NOT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_PayPalTransaction_TargetStatus' AND object_id = OBJECT_ID('tb_PayPalTransaction'))
BEGIN
    CREATE INDEX IX_tb_PayPalTransaction_TargetStatus ON tb_PayPalTransaction(TargetType, TargetCode, LocalStatus, ExpiresAtUtc);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_PayPalTransaction_ReconcileQueue' AND object_id = OBJECT_ID('tb_PayPalTransaction'))
BEGIN
    CREATE INDEX IX_tb_PayPalTransaction_ReconcileQueue ON tb_PayPalTransaction(LocalStatus, UpdatedAtUtc);
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID('tb_PayPalTransaction', 'U') IS NOT NULL
BEGIN
    DROP TABLE tb_PayPalTransaction;
END
");
    }
}
