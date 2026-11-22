using System;

namespace PickleballClub.Models;

public partial class TbPayPalTransaction
{
    public int PayPalTransactionId { get; set; }

    public string PublicAttemptCode { get; set; } = null!;

    /// <summary>
    /// "booking" | "order"
    /// </summary>
    public string TargetType { get; set; } = "booking";

    public int? BookingId { get; set; }

    public int? OrderId { get; set; }

    public string TargetCode { get; set; } = null!;

    public string? PayPalOrderId { get; set; }

    public string? PayPalCaptureId { get; set; }

    public string? PayPalRefundId { get; set; }

    public string? PayPalPayerId { get; set; }

    public string? PayPalPayerEmail { get; set; }

    /// <summary>
    /// Raw status returned by PayPal API (CREATED, APPROVED, COMPLETED, VOIDED, PAYER_ACTION_REQUIRED)
    /// </summary>
    public string? PayPalStatus { get; set; }

    /// <summary>
    /// Durable local state machine:
    /// Creating, AwaitingApproval, Capturing, Completed, Cancelled,
    /// ExpiredBeforeCapture, CreateFailed, CaptureFailed, AmbiguousTimeout,
    /// CapturedCompensationPending, RefundedAfterExpiry, Refunded,
    /// CompensationRefundRequired, Superseded, SupersededAlreadyPaid
    /// </summary>
    public string LocalStatus { get; set; } = "Creating";

    public int AmountVnd { get; set; }

    public decimal AmountVndDec { get; set; }

    public decimal ExchangeRateVndPerUsd { get; set; }

    public decimal AmountUsd { get; set; }

    public string CurrencyCode { get; set; } = "USD";

    public DateTime RateSnapshotUtc { get; set; }

    public string CreateIdempotencyKey { get; set; } = null!;

    public string? CaptureIdempotencyKey { get; set; }

    public string? RefundIdempotencyKey { get; set; }

    public int? LedgerTransactionId { get; set; }

    public int? CompensationRefundLedgerTxId { get; set; }

    public string ReturnNonce { get; set; } = null!;

    public string? ApprovalUrl { get; set; }

    public int? ActorAccountId { get; set; }

    public string? CreatedBy { get; set; }

    public string? ClientIpHash { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CapturedAtUtc { get; set; }

    public DateTime? RefundedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(2);

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public string? SanitizedSummaryJson { get; set; }

    public byte[]? RowVersion { get; set; }

    public virtual TbBooking? Booking { get; set; }

    public virtual TbOrder? Order { get; set; }

    public virtual TbPaymentTransaction? LedgerTransaction { get; set; }

    public virtual TbPaymentTransaction? CompensationRefundLedgerTx { get; set; }
}
