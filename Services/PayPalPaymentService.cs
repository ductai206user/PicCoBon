using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;
using PickleballClub.Models;

namespace PickleballClub.Services;

public sealed class PayPalCreateCheckoutInput
{
    public string TargetType { get; set; } = "booking";
    public string TargetCode { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string ReturnBaseUrl { get; set; } = string.Empty;
    public int? ActorAccountId { get; set; }
    public int? ActorRoleId { get; set; }
    public string? ActorUsername { get; set; }
    public string? GuestAccessToken { get; set; }
    public IReadOnlyDictionary<string, string>? GuestRecoveryTokens { get; set; }
    public string? IpAddress { get; set; }
}

public sealed class PayPalCheckoutActionInput
{
    public string StateToken { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public int? ActorAccountId { get; set; }
    public int? ActorRoleId { get; set; }
    public string? ActorUsername { get; set; }
    public string? GuestAccessToken { get; set; }
    public IReadOnlyDictionary<string, string>? GuestRecoveryTokens { get; set; }
    public string? IpAddress { get; set; }
}

public sealed class PayPalCheckoutResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; } = 200;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AttemptCode { get; set; }
    public string? TargetType { get; set; }
    public string? TargetCode { get; set; }
    public string? LocalStatus { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public string? PayPalRefundId { get; set; }
    public string? PayPalStatus { get; set; }
    public string? ApproveUrl { get; set; }
    public int AmountVnd { get; set; }
    public decimal AmountUsd { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal ExchangeRateVndPerUsd { get; set; }
    public bool NeedsReconciliation { get; set; }
    public bool IsIdempotentReplay { get; set; }
    public TbBooking? Booking { get; set; }
    public TbOrder? Order { get; set; }
    public TbPaymentTransaction? LedgerTransaction { get; set; }
    public TbPayPalTransaction? PayPalTransaction { get; set; }

    public static PayPalCheckoutResult Fail(
        string message,
        int statusCode = 400,
        string? errorCode = null,
        bool needsReconciliation = false,
        TbPayPalTransaction? ppTx = null) => new()
    {
        Success = false,
        StatusCode = statusCode,
        ErrorCode = errorCode,
        ErrorMessage = message,
        NeedsReconciliation = needsReconciliation,
        PayPalTransaction = ppTx,
        AttemptCode = ppTx?.PublicAttemptCode,
        TargetType = ppTx?.TargetType,
        TargetCode = ppTx?.TargetCode,
        LocalStatus = ppTx?.LocalStatus,
        PayPalOrderId = ppTx?.PayPalOrderId,
        PayPalCaptureId = ppTx?.PayPalCaptureId,
        PayPalRefundId = ppTx?.PayPalRefundId,
        PayPalStatus = ppTx?.PayPalStatus,
        AmountVnd = ppTx?.AmountVnd ?? 0,
        AmountUsd = ppTx?.AmountUsd ?? 0m,
        CurrencyCode = ppTx?.CurrencyCode ?? "USD",
        ExchangeRateVndPerUsd = ppTx?.ExchangeRateVndPerUsd ?? 0m
    };

    public static PayPalCheckoutResult Ok(
        TbPayPalTransaction ppTx,
        TbBooking? booking = null,
        TbOrder? order = null,
        TbPaymentTransaction? ledgerTx = null,
        bool replay = false) => new()
    {
        Success = true,
        StatusCode = 200,
        AttemptCode = ppTx.PublicAttemptCode,
        TargetType = ppTx.TargetType,
        TargetCode = ppTx.TargetCode,
        LocalStatus = ppTx.LocalStatus,
        PayPalOrderId = ppTx.PayPalOrderId,
        PayPalCaptureId = ppTx.PayPalCaptureId,
        PayPalRefundId = ppTx.PayPalRefundId,
        PayPalStatus = ppTx.PayPalStatus,
        ApproveUrl = ppTx.ApprovalUrl,
        AmountVnd = ppTx.AmountVnd,
        AmountUsd = ppTx.AmountUsd,
        CurrencyCode = ppTx.CurrencyCode,
        ExchangeRateVndPerUsd = ppTx.ExchangeRateVndPerUsd,
        IsIdempotentReplay = replay,
        NeedsReconciliation = ppTx.LocalStatus is "AmbiguousTimeout" or "CapturedCompensationPending" or "CompensationRefundRequired",
        Booking = booking,
        Order = order,
        LedgerTransaction = ledgerTx,
        PayPalTransaction = ppTx
    };
}

public sealed class PayPalStatePayload
{
    public string AttemptCode { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetCode { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
}

public sealed class PayPalPaymentService
{
    private readonly IPayPalClient _payPalClient;
    private readonly IOptionsMonitor<PayPalOptions> _optionsMonitor;
    private readonly IDataProtector _stateProtector;
    private readonly IDataProtector _guestOrderProtector;
    private readonly ILogger<PayPalPaymentService> _logger;

    private static readonly string[] ActiveLeaseStatuses =
    {
        "Creating",
        "Capturing",
        "AmbiguousTimeout",
        "CapturedCompensationPending"
    };

    public PayPalPaymentService(
        IPayPalClient payPalClient,
        IOptionsMonitor<PayPalOptions> optionsMonitor,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PayPalPaymentService> logger)
    {
        _payPalClient = payPalClient;
        _optionsMonitor = optionsMonitor;
        _stateProtector = dataProtectionProvider.CreateProtector("Spike.PayPal.State.v1");
        _guestOrderProtector = dataProtectionProvider.CreateProtector("Spike.GuestOrderProof.v1");
        _logger = logger;
    }

    public string ProtectGuestOrderProof(string orderCode, string normalizedPhone)
    {
        string payload = JsonSerializer.Serialize(new
        {
            code = orderCode.Trim().ToUpperInvariant(),
            phone = BookingService.NormalizePhone(normalizedPhone),
            exp = DateTime.UtcNow.AddDays(7)
        });
        return _guestOrderProtector.Protect(payload);
    }

    public bool VerifyGuestOrderProof(string? token, TbOrder order)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(order.Code))
        {
            return false;
        }

        try
        {
            string json = _guestOrderProtector.Unprotect(token.Trim());
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string code = root.TryGetProperty("code", out var c) ? (c.GetString() ?? string.Empty) : string.Empty;
            string phone = root.TryGetProperty("phone", out var p) ? (p.GetString() ?? string.Empty) : string.Empty;
            DateTime exp = root.TryGetProperty("exp", out var e) && e.TryGetDateTime(out var dt) ? dt : DateTime.MinValue;
            if (exp <= DateTime.UtcNow)
            {
                return false;
            }

            return string.Equals(code, order.Code, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(phone, BookingService.NormalizePhone(order.Phone), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public bool TryUnprotectState(string? protectedState, out PayPalStatePayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(protectedState))
        {
            return false;
        }

        try
        {
            string json = _stateProtector.Unprotect(protectedState.Trim());
            payload = JsonSerializer.Deserialize<PayPalStatePayload>(json);
            if (payload == null ||
                string.IsNullOrWhiteSpace(payload.AttemptCode) ||
                string.IsNullOrWhiteSpace(payload.TargetCode) ||
                string.IsNullOrWhiteSpace(payload.Nonce))
            {
                payload = null;
                return false;
            }

            // Reject expired checkout return states older than 6 hours
            if (payload.IssuedAtUtc < DateTime.UtcNow.AddHours(-6))
            {
                payload = null;
                return false;
            }

            return true;
        }
        catch
        {
            payload = null;
            return false;
        }
    }

    private string ProtectState(PayPalStatePayload payload) =>
        _stateProtector.Protect(JsonSerializer.Serialize(payload));

    public async Task<PayPalCheckoutResult> CreateCheckoutAsync(
        PickleballContext db,
        PayPalCreateCheckoutInput input,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var configCheck = opts.ValidateForCheckout();
        if (!configCheck.IsValid)
        {
            return PayPalCheckoutResult.Fail(configCheck.ErrorMessage, 503, configCheck.ErrorCode);
        }

        string targetType = (input.TargetType ?? "booking").Trim().ToLowerInvariant();
        if (targetType != "booking" && targetType != "order")
        {
            return PayPalCheckoutResult.Fail("Loại đối tượng thanh toán PayPal không hợp lệ ('booking' hoặc 'order').", 400);
        }

        string targetCode = (input.TargetCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(targetCode))
        {
            return PayPalCheckoutResult.Fail("Thiếu mã đơn hàng / mã đặt sân cần thanh toán.", 400);
        }

        string cleanIdem = IdempotencyService.NormalizeKey(input.IdempotencyKey) ?? string.Empty;
        if (string.IsNullOrEmpty(cleanIdem))
        {
            return PayPalCheckoutResult.Fail("Thiếu khóa IdempotencyKey cho giao dịch PayPal.", 400);
        }

        string cleanBaseUrl = (input.ReturnBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(cleanBaseUrl, UriKind.Absolute, out _))
        {
            return PayPalCheckoutResult.Fail("Origin URL trả về sau khi thanh toán không hợp lệ.", 400);
        }

        DateTime utcNow = DateTime.UtcNow;
        bool isRelational = db.Database.IsRelational();

        // =========================================================================
        // PHASE A: Short Serializable DB transaction (validate auth, balance, lease)
        // Strictly NO external HTTP calls inside this transaction!
        // =========================================================================
        TbPayPalTransaction? pendingRecord = null;
        string? protectedState = null;
        string? checkoutDescription = null;

        await using (var txA = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null)
        {
            // Check IdempotencyKey collision / replay first
            var existingByIdem = await db.TbPayPalTransactions
                .FirstOrDefaultAsync(x => x.CreateIdempotencyKey == cleanIdem, cancellationToken);
            if (existingByIdem != null)
            {
                bool matches =
                    string.Equals(existingByIdem.TargetType, targetType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existingByIdem.TargetCode, targetCode, StringComparison.OrdinalIgnoreCase) &&
                    (!input.ActorAccountId.HasValue || !existingByIdem.ActorAccountId.HasValue || existingByIdem.ActorAccountId == input.ActorAccountId);

                if (!matches)
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail(
                        "Khóa IdempotencyKey đã được sử dụng cho một phiên thanh toán PayPal khác.",
                        409,
                        IdempotencyService.ConflictErrorCode);
                }

                if (existingByIdem.LocalStatus == "AwaitingApproval" &&
                    existingByIdem.ExpiresAtUtc > utcNow &&
                    !string.IsNullOrEmpty(existingByIdem.ApprovalUrl))
                {
                    if (txA != null) await txA.CommitAsync(cancellationToken);
                    return PayPalCheckoutResult.Ok(existingByIdem, replay: true);
                }

                if (existingByIdem.LocalStatus == "Creating")
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail(
                        "Phiên khởi tạo đơn PayPal trước đó đang được xử lý, vui lòng đợi giây lát.",
                        409,
                        "PAYPAL_CREATE_IN_PROGRESS");
                }

                if (existingByIdem.LocalStatus == "Completed")
                {
                    if (txA != null) await txA.CommitAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail(
                        "Giao dịch PayPal này đã hoàn tất thanh toán trước đó.",
                        409,
                        "ALREADY_PAID",
                        ppTx: existingByIdem);
                }
            }

            TbBooking? booking = null;
            TbOrder? order = null;
            int remainingVnd = 0;

            if (targetType == "booking")
            {
                if (isRelational)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "SELECT BookingId FROM tb_Booking WITH (UPDLOCK, HOLDLOCK) WHERE BookingCode = {0}",
                        targetCode);
                }

                booking = await db.TbBookings
                    .Include(b => b.Court)
                    .FirstOrDefaultAsync(b => b.BookingCode == targetCode, cancellationToken);
                if (booking == null)
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail("Không tìm thấy mã đặt sân.", 404);
                }

                if (!AuthorizeBookingCaller(booking, input.ActorAccountId, input.ActorRoleId, input.GuestAccessToken, input.GuestRecoveryTokens))
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail(
                        "Bạn không có quyền khởi tạo thanh toán cho vé đặt sân này.",
                        403,
                        "FORBIDDEN");
                }

                if (booking.Status is "cancelled" or "rejected" or "expired")
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail(
                        $"Ca đặt sân đang ở trạng thái '{booking.Status}', không thể thanh toán.",
                        409,
                        "BOOKING_INACTIVE");
                }

                if (booking.Status == "pending" && booking.HoldExpiresAtUtc.HasValue && booking.HoldExpiresAtUtc.Value <= utcNow)
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail(
                        "Thời gian giữ chỗ ca đặt sân đã hết hạn. Vui lòng tạo đặt sân mới.",
                        409,
                        "BOOKING_HOLD_EXPIRED");
                }

                int amountDue = booking.AmountDue > 0
                    ? booking.AmountDue
                    : Math.Max(0, booking.CourtFee - booking.DiscountAmount + booking.PosTotal);
                int netPaid = await BookingService.ComputeBookingLedgerNetPaidAsync(db, booking.BookingId);
                remainingVnd = Math.Max(0, amountDue - netPaid);
                checkoutDescription = $"Spike OS Booking {booking.BookingCode} ({booking.Court?.CourtName ?? $"Court #{booking.CourtId}"} {booking.BookingDate} {booking.StartTime})";
            }
            else
            {
                if (isRelational)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "SELECT OrderId FROM tb_Order WITH (UPDLOCK, HOLDLOCK) WHERE Code = {0}",
                        targetCode);
                }

                order = await db.TbOrders.FirstOrDefaultAsync(o => o.Code == targetCode, cancellationToken);
                if (order == null)
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail("Không tìm thấy đơn hàng Pro Shop.", 404);
                }

                if (!AuthorizeOrderCaller(order, input.ActorAccountId, input.ActorRoleId, input.GuestAccessToken, input.GuestRecoveryTokens))
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail(
                        "Bạn không có quyền khởi tạo thanh toán cho đơn Pro Shop này.",
                        403,
                        "FORBIDDEN");
                }

                if (order.BookingId.HasValue)
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail(
                        $"Đơn hàng {order.Code} đã gộp vào hóa đơn sân {order.BookingCode}. Vui lòng thanh toán trực tiếp trên mã đặt sân để tránh thu trùng.",
                        409,
                        "ORDER_MERGED_TO_BOOKING");
                }

                if (order.Status is "cancelled" or "rejected")
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail("Đơn hàng Pro Shop đã hủy, không thể thanh toán.", 409, "ORDER_INACTIVE");
                }

                int totalDue = order.TotalAmount ?? 0;
                int netPaid = await OrderService.ComputeOrderLedgerNetPaidAsync(db, order.OrderId);
                remainingVnd = Math.Max(0, totalDue - netPaid);
                checkoutDescription = $"Spike OS Pro Shop Order {order.Code}";
            }

            if (remainingVnd <= 0)
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail("Đơn hàng / đặt sân này đã thanh toán đủ 100%.", 409, "ALREADY_PAID");
            }

            // Concurrency & duplicate protection across different IdempotencyKeys (Rule 7)
            var activeAttempt = await db.TbPayPalTransactions
                .Where(x => x.TargetCode == targetCode &&
                            x.TargetType == targetType &&
                            ActiveLeaseStatuses.Contains(x.LocalStatus))
                .FirstOrDefaultAsync(cancellationToken);

            if (activeAttempt != null)
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail(
                    "Đơn này đang có một giao dịch PayPal đang xử lý hoặc chờ đối soát. Vui lòng hoàn tất hoặc kiểm tra đối soát trước khi tạo đơn mới.",
                    409,
                    "PAYPAL_CAPTURE_IN_PROGRESS",
                    ppTx: activeAttempt);
            }

            // Supersede any abandoned AwaitingApproval attempt with a different IdempotencyKey so only one live session exists
            var oldAwaiting = await db.TbPayPalTransactions
                .Where(x => x.TargetCode == targetCode &&
                            x.TargetType == targetType &&
                            x.LocalStatus == "AwaitingApproval")
                .ToListAsync(cancellationToken);
            foreach (var stale in oldAwaiting)
            {
                stale.LocalStatus = "Superseded";
                stale.UpdatedAtUtc = utcNow;
                stale.LastErrorMessage = "Superseded by newer PayPal checkout attempt";
            }

            decimal amountUsd = opts.ConvertVndToUsd(remainingVnd);
            string attemptCode = $"PP-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
            string nonce = Guid.NewGuid().ToString("N")[..16];

            var statePayload = new PayPalStatePayload
            {
                AttemptCode = attemptCode,
                TargetType = targetType,
                TargetCode = targetCode,
                Nonce = nonce,
                IssuedAtUtc = utcNow
            };
            protectedState = ProtectState(statePayload);

            DateTime attemptExpiry = utcNow.AddMinutes(30);
            if (booking?.HoldExpiresAtUtc is DateTime holdExp && holdExp > utcNow && holdExp < attemptExpiry)
            {
                attemptExpiry = holdExp;
            }

            pendingRecord = new TbPayPalTransaction
            {
                PublicAttemptCode = attemptCode,
                TargetType = targetType,
                BookingId = booking?.BookingId,
                OrderId = order?.OrderId,
                TargetCode = targetCode,
                LocalStatus = "Creating",
                AmountVnd = remainingVnd,
                AmountVndDec = remainingVnd,
                ExchangeRateVndPerUsd = opts.VndPerUsd,
                AmountUsd = amountUsd,
                CurrencyCode = "USD",
                RateSnapshotUtc = utcNow,
                CreateIdempotencyKey = cleanIdem,
                ReturnNonce = nonce,
                ActorAccountId = input.ActorAccountId,
                CreatedBy = input.ActorUsername ?? (booking?.Phone ?? order?.Phone ?? "guest"),
                ClientIpHash = HashIp(input.IpAddress),
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow,
                ExpiresAtUtc = attemptExpiry
            };

            db.TbPayPalTransactions.Add(pendingRecord);
            await db.SaveChangesAsync(cancellationToken);
            if (txA != null) await txA.CommitAsync(cancellationToken);
        }

        // =========================================================================
        // EXTERNAL HTTP PHASE: Create PayPal Order v2 (Zero SQL locks held!)
        // =========================================================================
        string encodedState = Uri.EscapeDataString(protectedState!);
        string returnUrl = $"{cleanBaseUrl}/Payment/PayPalReturn?state={encodedState}";
        string cancelUrl = $"{cleanBaseUrl}/Payment/PayPalCancel?state={encodedState}";

        var createReq = new PayPalCreateOrderRequest(
            AttemptCode: pendingRecord.PublicAttemptCode,
            TargetCode: pendingRecord.TargetCode,
            Description: checkoutDescription ?? pendingRecord.TargetCode,
            AmountUsd: pendingRecord.AmountUsd,
            CurrencyCode: pendingRecord.CurrencyCode,
            ReturnUrl: returnUrl,
            CancelUrl: cancelUrl,
            BrandName: opts.BrandName,
            RequestId: $"create-{pendingRecord.PublicAttemptCode}");

        PayPalCreateOrderResponse apiRes;
        try
        {
            apiRes = await _payPalClient.CreateOrderAsync(createReq, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error calling PayPal CreateOrderAsync for attempt {AttemptCode}", pendingRecord.PublicAttemptCode);
            apiRes = new PayPalCreateOrderResponse(false, null, null, null, null, 502, "PAYPAL_NETWORK_ERROR", ex.Message);
        }

        // =========================================================================
        // PHASE B: Short DB transaction saving ApproveUrl & PayPalOrderId or failure
        // =========================================================================
        db.ChangeTracker.Clear();
        await using (var txB = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null)
        {
            var record = await db.TbPayPalTransactions.FirstAsync(x => x.PayPalTransactionId == pendingRecord.PayPalTransactionId, cancellationToken);
            record.UpdatedAtUtc = DateTime.UtcNow;

            if (apiRes.Success && !string.IsNullOrWhiteSpace(apiRes.PayPalOrderId) && !string.IsNullOrWhiteSpace(apiRes.ApproveUrl))
            {
                record.PayPalOrderId = apiRes.PayPalOrderId;
                record.PayPalStatus = apiRes.PayPalStatus ?? "CREATED";
                record.ApprovalUrl = apiRes.ApproveUrl;
                record.SanitizedSummaryJson = apiRes.SanitizedSummaryJson;
                record.LocalStatus = "AwaitingApproval";
                record.LastErrorCode = null;
                record.LastErrorMessage = null;

                db.TbAuditLogs.Add(new TbAuditLog
                {
                    Action = "PAYPAL_CHECKOUT_CREATED",
                    EntityType = "PayPalTransaction",
                    EntityId = record.PublicAttemptCode,
                    ActorAccountId = input.ActorAccountId,
                    ActorUsername = input.ActorUsername,
                    Details = $"Target={record.TargetType}:{record.TargetCode}, AmountVnd={record.AmountVnd}, AmountUsd={PayPalOptions.FormatUsdInvariant(record.AmountUsd)}, Rate={record.ExchangeRateVndPerUsd}, PayPalOrderId={record.PayPalOrderId}",
                    IpAddress = input.IpAddress,
                    CreatedAt = BookingService.GetVietnamNow()
                });

                await db.SaveChangesAsync(cancellationToken);
                if (txB != null) await txB.CommitAsync(cancellationToken);
                return PayPalCheckoutResult.Ok(record);
            }

            string errMsg = TruncateError(apiRes.ErrorMessage ?? "Không thể khởi tạo đơn thanh toán PayPal Sandbox.") ?? "Không thể khởi tạo đơn thanh toán PayPal Sandbox.";
            record.LocalStatus = "CreateFailed";
            record.LastErrorCode = apiRes.ErrorCode ?? "PAYPAL_CREATE_FAILED";
            record.LastErrorMessage = errMsg;
            await db.SaveChangesAsync(cancellationToken);
            if (txB != null) await txB.CommitAsync(cancellationToken);

            int status = apiRes.HttpStatusCode is >= 400 and < 600 ? apiRes.HttpStatusCode : 502;
            return PayPalCheckoutResult.Fail(errMsg, status, record.LastErrorCode, ppTx: record);
        }
    }

    public async Task<PayPalCheckoutResult> CaptureCheckoutAsync(
        PickleballContext db,
        PayPalCheckoutActionInput input,
        CancellationToken cancellationToken = default)
    {
        if (!TryUnprotectState(input.StateToken, out var statePayload) || statePayload == null)
        {
            return PayPalCheckoutResult.Fail(
                "Tham số phiên thanh toán PayPal không hợp lệ hoặc đã bị thay đổi.",
                400,
                "INVALID_PAYPAL_STATE");
        }

        return await CaptureOrReconcileAttemptInternalAsync(
            db,
            attemptCode: statePayload.AttemptCode,
            expectedNonce: statePayload.Nonce,
            expectedPayPalOrderId: input.PayPalOrderId,
            actorAccountId: input.ActorAccountId,
            actorRoleId: input.ActorRoleId,
            actorUsername: input.ActorUsername,
            guestAccessToken: input.GuestAccessToken,
            guestRecoveryTokens: input.GuestRecoveryTokens,
            ipAddress: input.IpAddress,
            requireCallerAuth: true,
            allowCaptureApproved: true,
            cancellationToken: cancellationToken);
    }

    public async Task<PayPalCheckoutResult> CancelCheckoutAsync(
        PickleballContext db,
        PayPalCheckoutActionInput input,
        CancellationToken cancellationToken = default)
    {
        if (!TryUnprotectState(input.StateToken, out var statePayload) || statePayload == null)
        {
            return PayPalCheckoutResult.Fail(
                "Tham số phiên hủy PayPal không hợp lệ hoặc đã bị thay đổi.",
                400,
                "INVALID_PAYPAL_STATE");
        }

        bool isRelational = db.Database.IsRelational();
        DateTime utcNow = DateTime.UtcNow;

        TbPayPalTransaction? checkAttempt;
        await using (var tx = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null)
        {
            checkAttempt = await db.TbPayPalTransactions
                .FirstOrDefaultAsync(x => x.PublicAttemptCode == statePayload.AttemptCode, cancellationToken);
            if (checkAttempt == null || !string.Equals(checkAttempt.ReturnNonce, statePayload.Nonce, StringComparison.Ordinal))
            {
                if (tx != null) await tx.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail("Không tìm thấy giao dịch PayPal cần hủy.", 404, "PAYPAL_ATTEMPT_NOT_FOUND");
            }

            bool authorized = await AuthorizeAttemptAsync(
                db, checkAttempt, input.ActorAccountId, input.ActorRoleId, input.GuestAccessToken, input.GuestRecoveryTokens, cancellationToken);
            if (!authorized)
            {
                if (tx != null) await tx.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail("Bạn không có quyền thao tác trên phiên thanh toán này.", 403, "FORBIDDEN");
            }

            if (checkAttempt.LocalStatus == "Completed")
            {
                var (bk, ord) = await LoadTargetAsync(db, checkAttempt, cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);
                return PayPalCheckoutResult.Ok(checkAttempt, bk, ord, replay: true);
            }

            if (checkAttempt.LocalStatus is "Capturing" or "AmbiguousTimeout" or "CapturedCompensationPending")
            {
                if (tx != null) await tx.RollbackAsync(cancellationToken);
                // Reconcile safely instead of blindly cancelling if capture might have happened
                return await CaptureOrReconcileAttemptInternalAsync(
                    db,
                    checkAttempt.PublicAttemptCode,
                    statePayload.Nonce,
                    input.PayPalOrderId,
                    input.ActorAccountId,
                    input.ActorRoleId,
                    input.ActorUsername,
                    input.GuestAccessToken,
                    input.GuestRecoveryTokens,
                    input.IpAddress,
                    requireCallerAuth: true,
                    allowCaptureApproved: false,
                    cancellationToken: cancellationToken);
            }

            checkAttempt.LocalStatus = "Cancelled";
            checkAttempt.UpdatedAtUtc = utcNow;
            db.TbAuditLogs.Add(new TbAuditLog
            {
                Action = "PAYPAL_CHECKOUT_CANCELLED",
                EntityType = "PayPalTransaction",
                EntityId = checkAttempt.PublicAttemptCode,
                ActorAccountId = input.ActorAccountId,
                ActorUsername = input.ActorUsername,
                Details = $"Target={checkAttempt.TargetType}:{checkAttempt.TargetCode}, PayPalOrderId={checkAttempt.PayPalOrderId}",
                IpAddress = input.IpAddress,
                CreatedAt = BookingService.GetVietnamNow()
            });

            var (targetBk, targetOrd) = await LoadTargetAsync(db, checkAttempt, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            return PayPalCheckoutResult.Ok(checkAttempt, targetBk, targetOrd);
        }
    }

    public async Task<PayPalCheckoutResult> ReconcileByAttemptCodeAsync(
        PickleballContext db,
        string attemptCode,
        int? actorAccountId,
        int? actorRoleId,
        string? actorUsername,
        string? guestAccessToken,
        IReadOnlyDictionary<string, string>? guestRecoveryTokens,
        string? ipAddress,
        bool requireCallerAuth = true,
        CancellationToken cancellationToken = default)
    {
        return await CaptureOrReconcileAttemptInternalAsync(
            db,
            attemptCode: attemptCode,
            expectedNonce: null,
            expectedPayPalOrderId: null,
            actorAccountId: actorAccountId,
            actorRoleId: actorRoleId,
            actorUsername: actorUsername,
            guestAccessToken: guestAccessToken,
            guestRecoveryTokens: guestRecoveryTokens,
            ipAddress: ipAddress,
            requireCallerAuth: requireCallerAuth,
            allowCaptureApproved: true,
            cancellationToken: cancellationToken);
    }

    public async Task<PayPalCheckoutResult> RefundCapturedPaymentAsync(
        PickleballContext db,
        string attemptCode,
        string? reason,
        string? idempotencyKey,
        int actorAccountId,
        string actorUsername,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var configCheck = opts.ValidateForCheckout();
        if (!configCheck.IsValid)
        {
            return PayPalCheckoutResult.Fail(configCheck.ErrorMessage, 503, configCheck.ErrorCode);
        }

        string cleanIdem = IdempotencyService.NormalizeKey(idempotencyKey) ?? $"adm-rfd-{attemptCode}-{Guid.NewGuid():N}"[..40];
        bool isRelational = db.Database.IsRelational();
        DateTime utcNow = DateTime.UtcNow;

        TbPayPalTransaction? ppTx;
        await using (var txA = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null)
        {
            if (isRelational)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT PayPalTransactionId FROM tb_PayPalTransaction WITH (UPDLOCK, HOLDLOCK) WHERE PublicAttemptCode = {0}",
                    attemptCode);
            }

            ppTx = await db.TbPayPalTransactions
                .FirstOrDefaultAsync(x => x.PublicAttemptCode == attemptCode, cancellationToken);
            if (ppTx == null)
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail("Không tìm thấy giao dịch PayPal.", 404);
            }

            if (ppTx.LocalStatus is "Refunded" or "RefundedAfterExpiry")
            {
                var (b0, o0) = await LoadTargetAsync(db, ppTx, cancellationToken);
                if (txA != null) await txA.CommitAsync(cancellationToken);
                return PayPalCheckoutResult.Ok(ppTx, b0, o0, replay: true);
            }

            if (ppTx.LocalStatus is not ("Completed" or "CapturedCompensationPending" or "CompensationRefundRequired") ||
                string.IsNullOrWhiteSpace(ppTx.PayPalCaptureId))
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail(
                    $"Chỉ có thể hoàn tiền giao dịch đã thu thành công từ PayPal (trạng thái hiện tại: {ppTx.LocalStatus}).",
                    409);
            }

            int currentNetPaid = await ComputeTargetNetPaidAsync(db, ppTx, cancellationToken);
            if (currentNetPaid < ppTx.AmountVnd)
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail(
                    $"Số tiền thực thu hiện tại ({currentNetPaid:N0} đ) nhỏ hơn số tiền cần hoàn của giao dịch PayPal ({ppTx.AmountVnd:N0} đ).",
                    409);
            }

            ppTx.RefundIdempotencyKey ??= cleanIdem;
            ppTx.UpdatedAtUtc = utcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (txA != null) await txA.CommitAsync(cancellationToken);
        }

        // External HTTP Refund OUTSIDE any SQL transaction!
        string noteToPayer = string.IsNullOrWhiteSpace(reason)
            ? $"Refund Spike OS {ppTx.TargetCode}"
            : reason.Trim()[..Math.Min(reason.Trim().Length, 120)];

        var refundRes = await _payPalClient.RefundCaptureAsync(
            ppTx.PayPalCaptureId!,
            ppTx.AmountUsd,
            ppTx.CurrencyCode,
            noteToPayer,
            ppTx.RefundIdempotencyKey!,
            cancellationToken);

        if (!refundRes.Success || string.IsNullOrWhiteSpace(refundRes.RefundId))
        {
            await using var txErr = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
            var rec = await db.TbPayPalTransactions.FirstAsync(x => x.PayPalTransactionId == ppTx.PayPalTransactionId, cancellationToken);
            string errMsg = TruncateError(refundRes.ErrorMessage ?? "PayPal hoàn tiền không thành công.") ?? "PayPal hoàn tiền không thành công.";
            rec.LastErrorCode = refundRes.ErrorCode ?? "PAYPAL_REFUND_FAILED";
            rec.LastErrorMessage = errMsg;
            rec.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (txErr != null) await txErr.CommitAsync(cancellationToken);

            return PayPalCheckoutResult.Fail(
                errMsg,
                refundRes.HttpStatusCode is >= 400 and < 600 ? refundRes.HttpStatusCode : 502,
                rec.LastErrorCode,
                needsReconciliation: true,
                ppTx: rec);
        }

        return await FinalizePayPalRefundInLedgerAsync(
            db,
            ppTx.PayPalTransactionId,
            refundRes,
            isCompensationAfterExpiry: ppTx.LocalStatus is "CapturedCompensationPending" or "CompensationRefundRequired",
            reason: noteToPayer,
            actorAccountId: actorAccountId,
            actorUsername: actorUsername,
            ipAddress: ipAddress,
            cancellationToken: cancellationToken);
    }

    public async Task<(bool Ok, int HttpStatus, string Message)> ProcessWebhookAsync(
        PickleballContext db,
        PayPalWebhookHeaders headers,
        string rawJsonBody,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawJsonBody))
        {
            return (false, 400, "Empty PayPal webhook body");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawJsonBody);
        }
        catch
        {
            return (false, 400, "Malformed PayPal webhook JSON");
        }

        using (doc)
        {
            bool verified = await _payPalClient.VerifyWebhookSignatureAsync(headers, doc.RootElement, cancellationToken);
            if (!verified)
            {
                _logger.LogWarning("Rejected unverified PayPal webhook event. TransmissionId={TransmissionId}", headers.TransmissionId);
                return (false, 401, "WEBHOOK_SIGNATURE_INVALID");
            }

            var root = doc.RootElement;
            string eventType = root.TryGetProperty("event_type", out var et) ? (et.GetString() ?? string.Empty) : string.Empty;
            string? relatedOrderId = null;
            string? captureId = null;

            if (root.TryGetProperty("resource", out var resEl) && resEl.ValueKind == JsonValueKind.Object)
            {
                if (eventType == "CHECKOUT.ORDER.APPROVED")
                {
                    relatedOrderId = resEl.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                }
                else if (eventType.StartsWith("PAYMENT.CAPTURE.", StringComparison.OrdinalIgnoreCase))
                {
                    captureId = resEl.TryGetProperty("id", out var cidEl) ? cidEl.GetString() : null;
                    if (resEl.TryGetProperty("supplementary_data", out var supEl) &&
                        supEl.TryGetProperty("related_ids", out var relEl) &&
                        relEl.TryGetProperty("order_id", out var oidEl))
                    {
                        relatedOrderId = oidEl.GetString();
                    }
                }
            }

            TbPayPalTransaction? attempt = null;
            if (!string.IsNullOrWhiteSpace(relatedOrderId))
            {
                attempt = await db.TbPayPalTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PayPalOrderId == relatedOrderId, cancellationToken);
            }
            if (attempt == null && !string.IsNullOrWhiteSpace(captureId))
            {
                attempt = await db.TbPayPalTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PayPalCaptureId == captureId, cancellationToken);
            }

            if (attempt == null)
            {
                return (true, 200, "Webhook verified; no matching local PayPal attempt");
            }

            await CaptureOrReconcileAttemptInternalAsync(
                db,
                attemptCode: attempt.PublicAttemptCode,
                expectedNonce: null,
                expectedPayPalOrderId: attempt.PayPalOrderId,
                actorAccountId: null,
                actorRoleId: 1,
                actorUsername: $"paypal_webhook:{eventType}",
                guestAccessToken: null,
                guestRecoveryTokens: null,
                ipAddress: null,
                requireCallerAuth: false,
                allowCaptureApproved: eventType is "CHECKOUT.ORDER.APPROVED" or "PAYMENT.CAPTURE.COMPLETED",
                cancellationToken: cancellationToken);

            return (true, 200, "Webhook processed");
        }
    }

    /// <summary>
    /// Safe background reconciliation sweep for AmbiguousTimeout, stuck Capturing locks,
    /// abandoned AwaitingApproval orders, and CompensationRefundRequired retries.
    /// </summary>
    public async Task<int> ReconcileStaleAttemptsAsync(
        PickleballContext db,
        DateTime? referenceUtcNow = null,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        if (!opts.ValidateForCheckout().IsValid)
        {
            return 0;
        }

        DateTime utcNow = referenceUtcNow ?? DateTime.UtcNow;
        DateTime staleCaptureCutoff = utcNow.AddMinutes(-2);
        DateTime staleAwaitingCutoff = utcNow.AddMinutes(-3);

        var candidates = await db.TbPayPalTransactions
            .AsNoTracking()
            .Where(x =>
                x.LocalStatus == "AmbiguousTimeout" ||
                x.LocalStatus == "CapturedCompensationPending" ||
                x.LocalStatus == "CompensationRefundRequired" ||
                (x.LocalStatus == "Capturing" && x.UpdatedAtUtc <= staleCaptureCutoff) ||
                (x.LocalStatus == "AwaitingApproval" &&
                 (x.ExpiresAtUtc <= utcNow || x.CreatedAtUtc <= staleAwaitingCutoff)))
            .OrderBy(x => x.UpdatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        int touched = 0;
        foreach (var cand in candidates)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                if (cand.LocalStatus is "CapturedCompensationPending" or "CompensationRefundRequired" &&
                    !string.IsNullOrWhiteSpace(cand.PayPalCaptureId))
                {
                    var refundRes = await _payPalClient.RefundCaptureAsync(
                        cand.PayPalCaptureId!,
                        cand.AmountUsd,
                        cand.CurrencyCode,
                        $"Auto compensation refund for {cand.TargetCode}",
                        $"comp-rfd-{cand.PublicAttemptCode}",
                        cancellationToken);

                    if (refundRes.Success && !string.IsNullOrWhiteSpace(refundRes.RefundId))
                    {
                        await FinalizePayPalRefundInLedgerAsync(
                            db,
                            cand.PayPalTransactionId,
                            refundRes,
                            isCompensationAfterExpiry: true,
                            reason: $"Tự động hoàn tiền PayPal do hết hạn giữ chỗ hoặc đã thanh toán ({cand.TargetCode})",
                            actorAccountId: null,
                            actorUsername: "paypal_reconciliation_worker",
                            ipAddress: null,
                            cancellationToken: cancellationToken);
                        touched++;
                    }
                    continue;
                }

                // Force stuck Capturing -> AmbiguousTimeout only if lease clearly aged out
                if (cand.LocalStatus == "Capturing" && cand.UpdatedAtUtc <= staleCaptureCutoff)
                {
                    await db.TbPayPalTransactions
                        .Where(x => x.PayPalTransactionId == cand.PayPalTransactionId && x.LocalStatus == "Capturing")
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.LocalStatus, "AmbiguousTimeout")
                            .SetProperty(x => x.UpdatedAtUtc, utcNow),
                            cancellationToken);
                }

                await CaptureOrReconcileAttemptInternalAsync(
                    db,
                    attemptCode: cand.PublicAttemptCode,
                    expectedNonce: null,
                    expectedPayPalOrderId: cand.PayPalOrderId,
                    actorAccountId: null,
                    actorRoleId: 1,
                    actorUsername: "paypal_reconciliation_worker",
                    guestAccessToken: null,
                    guestRecoveryTokens: null,
                    ipAddress: null,
                    requireCallerAuth: false,
                    allowCaptureApproved: true,
                    cancellationToken: cancellationToken);
                touched++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PayPal reconciliation sweep failed for {AttemptCode}", cand.PublicAttemptCode);
            }
        }

        return touched;
    }

    private async Task<PayPalCheckoutResult> CaptureOrReconcileAttemptInternalAsync(
        PickleballContext db,
        string attemptCode,
        string? expectedNonce,
        string? expectedPayPalOrderId,
        int? actorAccountId,
        int? actorRoleId,
        string? actorUsername,
        string? guestAccessToken,
        IReadOnlyDictionary<string, string>? guestRecoveryTokens,
        string? ipAddress,
        bool requireCallerAuth,
        bool allowCaptureApproved,
        CancellationToken cancellationToken)
    {
        var opts = _optionsMonitor.CurrentValue;
        var configCheck = opts.ValidateForCheckout();
        if (!configCheck.IsValid)
        {
            return PayPalCheckoutResult.Fail(configCheck.ErrorMessage, 503, configCheck.ErrorCode);
        }

        bool isRelational = db.Database.IsRelational();
        DateTime utcNow = DateTime.UtcNow;

        TbPayPalTransaction ppTx;
        string captureIdemKey;

        // =========================================================================
        // PHASE A: Short Serializable DB tx (auth + lease + pre-capture validation)
        // =========================================================================
        await using (var txA = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null)
        {
            if (isRelational)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT PayPalTransactionId FROM tb_PayPalTransaction WITH (UPDLOCK, HOLDLOCK) WHERE PublicAttemptCode = {0}",
                    attemptCode);
            }

            var loaded = await db.TbPayPalTransactions
                .FirstOrDefaultAsync(x => x.PublicAttemptCode == attemptCode, cancellationToken);
            if (loaded == null)
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail("Không tìm thấy giao dịch PayPal tương ứng.", 404, "PAYPAL_ATTEMPT_NOT_FOUND");
            }
            ppTx = loaded;

            if (!string.IsNullOrWhiteSpace(expectedNonce) &&
                !string.Equals(ppTx.ReturnNonce, expectedNonce, StringComparison.Ordinal))
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail("Mã phiên PayPal không khớp.", 400, "INVALID_PAYPAL_STATE");
            }

            if (!string.IsNullOrWhiteSpace(expectedPayPalOrderId) &&
                !string.Equals(ppTx.PayPalOrderId, expectedPayPalOrderId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail("PayPal Order ID không khớp với phiên khởi tạo ban đầu.", 400, "PAYPAL_ORDER_MISMATCH");
            }

            if (requireCallerAuth)
            {
                bool authorized = await AuthorizeAttemptAsync(
                    db, ppTx, actorAccountId, actorRoleId, guestAccessToken, guestRecoveryTokens, cancellationToken);
                if (!authorized)
                {
                    if (txA != null) await txA.RollbackAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail("Bạn không có quyền xác nhận giao dịch thanh toán này.", 403, "FORBIDDEN");
                }
            }

            if (ppTx.LocalStatus == "Completed" && ppTx.LedgerTransactionId.HasValue)
            {
                var (bk0, ord0) = await LoadTargetAsync(db, ppTx, cancellationToken);
                var ledger0 = await db.TbPaymentTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TransactionId == ppTx.LedgerTransactionId.Value, cancellationToken);
                if (txA != null) await txA.CommitAsync(cancellationToken);
                return PayPalCheckoutResult.Ok(ppTx, bk0, ord0, ledger0, replay: true);
            }

            if (ppTx.LocalStatus is "Refunded" or "RefundedAfterExpiry")
            {
                var (bkR, ordR) = await LoadTargetAsync(db, ppTx, cancellationToken);
                if (txA != null) await txA.CommitAsync(cancellationToken);
                return PayPalCheckoutResult.Ok(ppTx, bkR, ordR, replay: true);
            }

            if (ppTx.LocalStatus == "Capturing")
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail(
                    "Giao dịch PayPal đang được xử lý ở một yêu cầu khác. Vui lòng chờ hoàn tất và kiểm tra lại trạng thái.",
                    409,
                    "PAYPAL_CAPTURE_IN_PROGRESS",
                    ppTx: ppTx);
            }

            // Reject concurrent active capture attempts for the same target using different IdempotencyKeys
            bool anotherCapturing = await db.TbPayPalTransactions.AnyAsync(
                x => x.TargetCode == ppTx.TargetCode &&
                     x.TargetType == ppTx.TargetType &&
                     x.PayPalTransactionId != ppTx.PayPalTransactionId &&
                     (x.LocalStatus == "Capturing" || x.LocalStatus == "CapturedCompensationPending"),
                cancellationToken);
            if (anotherCapturing)
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail(
                    "Đơn hàng đang có một giao dịch PayPal khác đang thu tiền đồng thời.",
                    409,
                    "PAYPAL_CAPTURE_IN_PROGRESS",
                    ppTx: ppTx);
            }

            // Only pre-abort before calling PayPal if we have NOT already captured or timed out in PayPal
            bool mayHaveCapturedInPayPal = ppTx.LocalStatus is "AmbiguousTimeout" or "CapturedCompensationPending" or "CompensationRefundRequired" ||
                                           !string.IsNullOrWhiteSpace(ppTx.PayPalCaptureId);

            if (!mayHaveCapturedInPayPal)
            {
                var (targetValid, invalidCode, invalidMsg) = await EvaluateTargetEligibilityAsync(db, ppTx, utcNow, cancellationToken);
                if (!targetValid)
                {
                    ppTx.LocalStatus = invalidCode == "ALREADY_PAID" || invalidCode == "INSUFFICIENT_REMAINING"
                        ? "SupersededAlreadyPaid"
                        : "ExpiredBeforeCapture";
                    ppTx.LastErrorCode = invalidCode;
                    ppTx.LastErrorMessage = TruncateError(invalidMsg);
                    ppTx.UpdatedAtUtc = utcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    if (txA != null) await txA.CommitAsync(cancellationToken);
                    return PayPalCheckoutResult.Fail(invalidMsg, 409, invalidCode, ppTx: ppTx);
                }
            }

            if (string.IsNullOrWhiteSpace(ppTx.PayPalOrderId))
            {
                if (txA != null) await txA.RollbackAsync(cancellationToken);
                return PayPalCheckoutResult.Fail("Phiên PayPal chưa có mã đơn hàng PayPal hợp lệ.", 409, "PAYPAL_ORDER_MISSING", ppTx: ppTx);
            }

            ppTx.LocalStatus = "Capturing";
            ppTx.CaptureIdempotencyKey ??= $"cap-{ppTx.PublicAttemptCode}";
            ppTx.UpdatedAtUtc = utcNow;
            captureIdemKey = ppTx.CaptureIdempotencyKey;
            await db.SaveChangesAsync(cancellationToken);
            if (txA != null) await txA.CommitAsync(cancellationToken);
        }

        // =========================================================================
        // EXTERNAL HTTP PHASE: Check GetOrder -> Capture ONLY if APPROVED (Rule 3)
        // Zero SQL transactions or locks held during this network I/O!
        // =========================================================================
        PayPalOrderSnapshot? finalSnap = null;
        string? abortStatus = null;
        int abortHttp = 409;
        string? abortCode = null;
        string? abortMsg = null;

        try
        {
            var preSnap = await _payPalClient.GetOrderAsync(ppTx.PayPalOrderId!, cancellationToken);
            if (!preSnap.Success)
            {
                abortStatus = preSnap.IsAmbiguousTimeout ? "AmbiguousTimeout" : "AmbiguousTimeout";
                abortHttp = preSnap.HttpStatusCode is >= 400 and < 600 ? preSnap.HttpStatusCode : 504;
                abortCode = preSnap.ErrorCode ?? "PAYPAL_GET_ORDER_FAILED";
                abortMsg = preSnap.ErrorMessage ?? "Không thể kiểm tra trạng thái đơn PayPal. Vui lòng thử đối soát lại sau.";
            }
            else if (string.Equals(preSnap.OrderStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(preSnap.CaptureId))
            {
                // Already captured at PayPal (e.g. timeout recovery or retry) - never call /capture again!
                finalSnap = preSnap;
            }
            else if (string.Equals(preSnap.OrderStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowCaptureApproved)
                {
                    abortStatus = "AwaitingApproval";
                    abortHttp = 409;
                    abortCode = "PAYPAL_APPROVED_NOT_CAPTURED";
                    abortMsg = "Đơn PayPal đã được người mua chấp thuận nhưng chưa thực hiện thu tiền.";
                }
                else if (!AmountsMatch(ppTx, preSnap))
                {
                    abortStatus = "CaptureFailed";
                    abortHttp = 422;
                    abortCode = "PAYPAL_AMOUNT_MISMATCH";
                    abortMsg = $"Số tiền hoặc tiền tệ trên PayPal ({preSnap.AmountUsd} {preSnap.CurrencyCode}) không khớp với hóa đơn ({ppTx.AmountUsd} {ppTx.CurrencyCode}).";
                }
                else
                {
                    var capSnap = await _payPalClient.CaptureOrderAsync(ppTx.PayPalOrderId!, captureIdemKey, cancellationToken);
                    if (capSnap.Success &&
                        string.Equals(capSnap.OrderStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(capSnap.CaptureId))
                    {
                        finalSnap = capSnap;
                    }
                    else if (capSnap.IsAmbiguousTimeout)
                    {
                        // Immediately probe GetOrder once to see whether PayPal already completed capture
                        var probeSnap = await _payPalClient.GetOrderAsync(ppTx.PayPalOrderId!, cancellationToken);
                        if (probeSnap.Success &&
                            string.Equals(probeSnap.OrderStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(probeSnap.CaptureId))
                        {
                            finalSnap = probeSnap;
                        }
                        else
                        {
                            abortStatus = "AmbiguousTimeout";
                            abortHttp = 504;
                            abortCode = capSnap.ErrorCode ?? "PAYPAL_CAPTURE_TIMEOUT";
                            abortMsg = "Yêu cầu thu tiền PayPal bị quá thời gian phản hồi. Hệ thống đang bảo vệ giao dịch và sẽ đối soát tự động.";
                        }
                    }
                    else
                    {
                        abortStatus = "CaptureFailed";
                        abortHttp = capSnap.HttpStatusCode is >= 400 and < 600 ? capSnap.HttpStatusCode : 422;
                        abortCode = capSnap.ErrorCode ?? "PAYPAL_CAPTURE_FAILED";
                        abortMsg = capSnap.ErrorMessage ?? "PayPal từ chối thu tiền cho giao dịch này.";
                    }
                }
            }
            else if (string.Equals(preSnap.OrderStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
            {
                abortStatus = "Cancelled";
                abortHttp = 409;
                abortCode = "PAYPAL_ORDER_VOIDED";
                abortMsg = "Phiên thanh toán PayPal này đã bị hủy hoặc hết hiệu lực.";
            }
            else
            {
                // PAYER_ACTION_REQUIRED, CREATED, SAVED, etc. -> NEVER call /capture! (Rule 3)
                abortStatus = ppTx.ExpiresAtUtc <= DateTime.UtcNow ? "ExpiredBeforeCapture" : "AwaitingApproval";
                abortHttp = 409;
                abortCode = "PAYPAL_APPROVAL_REQUIRED";
                abortMsg = $"Giao dịch PayPal đang ở trạng thái '{preSnap.OrderStatus ?? "CREATED"}', chưa hoàn tất bước xác nhận thanh toán từ người mua.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PayPal capture/get exception for attempt {AttemptCode}", ppTx.PublicAttemptCode);
            abortStatus = "AmbiguousTimeout";
            abortHttp = 504;
            abortCode = "PAYPAL_NETWORK_EXCEPTION";
            abortMsg = "Mất kết nối tạm thời tới PayPal Sandbox trong quá trình thu tiền. Vui lòng nhấn Đối soát để kiểm tra.";
        }

        if (finalSnap == null)
        {
            db.ChangeTracker.Clear();
            await using var txAbort = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
            var rec = await db.TbPayPalTransactions.FirstAsync(x => x.PayPalTransactionId == ppTx.PayPalTransactionId, cancellationToken);
            rec.LocalStatus = abortStatus ?? "AmbiguousTimeout";
            rec.LastErrorCode = abortCode;
            rec.LastErrorMessage = TruncateError(abortMsg);
            rec.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (txAbort != null) await txAbort.CommitAsync(cancellationToken);

            return PayPalCheckoutResult.Fail(
                abortMsg ?? "Chưa thể hoàn tất thu tiền PayPal.",
                abortHttp,
                abortCode,
                needsReconciliation: rec.LocalStatus == "AmbiguousTimeout",
                ppTx: rec);
        }

        // Verify capture amount & currency STRICTLY before trusting COMPLETED capture
        if (!AmountsMatch(ppTx, finalSnap))
        {
            db.ChangeTracker.Clear();
            await using var txMismatch = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            var rec = await db.TbPayPalTransactions.FirstAsync(x => x.PayPalTransactionId == ppTx.PayPalTransactionId, cancellationToken);
            rec.PayPalCaptureId = finalSnap.CaptureId;
            rec.PayPalStatus = finalSnap.OrderStatus ?? "COMPLETED";
            rec.PayPalPayerId = finalSnap.PayerId;
            rec.PayPalPayerEmail = finalSnap.PayerEmail;
            rec.CapturedAtUtc ??= DateTime.UtcNow;
            rec.SanitizedSummaryJson = finalSnap.SanitizedSummaryJson;
            rec.LocalStatus = "CompensationRefundRequired";
            rec.LastErrorCode = "PAYPAL_CAPTURE_MISMATCH";
            rec.LastErrorMessage = $"Số tiền capture PayPal ({finalSnap.AmountUsd} {finalSnap.CurrencyCode}) lệch với đơn hàng ({rec.AmountUsd} {rec.CurrencyCode}).";
            rec.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (txMismatch != null) await txMismatch.CommitAsync(cancellationToken);

            return PayPalCheckoutResult.Fail(rec.LastErrorMessage, 422, "PAYPAL_CAPTURE_MISMATCH", needsReconciliation: true, ppTx: rec);
        }

        // =========================================================================
        // PHASE B: Persist capture evidence + ledger transaction atomically
        // If hold expired or balance changed during PayPal redirect -> persist
        // capture evidence FIRST, then release SQL TX and execute compensation refund!
        // =========================================================================
        bool compensationNeeded = false;
        string compensationReason = string.Empty;
        TbBooking? finalBooking = null;
        TbOrder? finalOrder = null;
        TbPaymentTransaction? postedLedgerTx = null;
        TbPayPalTransaction savedRecord;

        db.ChangeTracker.Clear();
        await using (var txB = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null)
        {
            if (isRelational)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT PayPalTransactionId FROM tb_PayPalTransaction WITH (UPDLOCK, HOLDLOCK) WHERE PayPalTransactionId = {0}",
                    ppTx.PayPalTransactionId);
            }

            savedRecord = await db.TbPayPalTransactions
                .FirstAsync(x => x.PayPalTransactionId == ppTx.PayPalTransactionId, cancellationToken);

            // Save PayPal capture evidence unconditionally so it is NEVER lost
            savedRecord.PayPalCaptureId = finalSnap.CaptureId;
            savedRecord.PayPalStatus = finalSnap.OrderStatus ?? "COMPLETED";
            savedRecord.PayPalPayerId = finalSnap.PayerId;
            savedRecord.PayPalPayerEmail = finalSnap.PayerEmail;
            savedRecord.CapturedAtUtc ??= DateTime.UtcNow;
            savedRecord.SanitizedSummaryJson = finalSnap.SanitizedSummaryJson;
            savedRecord.LastErrorCode = null;
            savedRecord.LastErrorMessage = null;
            savedRecord.UpdatedAtUtc = DateTime.UtcNow;

            if (savedRecord.LocalStatus == "Completed" && savedRecord.LedgerTransactionId.HasValue)
            {
                var (bkEx, ordEx) = await LoadTargetAsync(db, savedRecord, cancellationToken);
                var ledEx = await db.TbPaymentTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TransactionId == savedRecord.LedgerTransactionId.Value, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                if (txB != null) await txB.CommitAsync(cancellationToken);
                return PayPalCheckoutResult.Ok(savedRecord, bkEx, ordEx, ledEx, replay: true);
            }

            var vnNow = BookingService.GetVietnamNow();
            DateTime postUtcNow = DateTime.UtcNow;

            var (stillEligible, eligCode, eligMsg) = await EvaluateTargetEligibilityAsync(db, savedRecord, postUtcNow, cancellationToken);

            // Always write receipt row in TbPaymentTransaction so the captured cash is reconciled against financial ledger!
            if (savedRecord.TargetType == "booking" && savedRecord.BookingId.HasValue)
            {
                finalBooking = await db.TbBookings
                    .Include(b => b.Court)
                    .FirstAsync(b => b.BookingId == savedRecord.BookingId.Value, cancellationToken);

                postedLedgerTx = await BookingService.PostPayPalLedgerInCurrentTxAsync(
                    db,
                    finalBooking,
                    savedRecord.AmountVnd,
                    finalSnap.CaptureId!,
                    savedRecord.PayPalOrderId!,
                    note: $"PayPal Sandbox {savedRecord.PublicAttemptCode} - {PayPalOptions.FormatUsdInvariant(savedRecord.AmountUsd)} USD (@{savedRecord.ExchangeRateVndPerUsd:0})",
                    actorAccountId: actorAccountId,
                    actorUsername: actorUsername,
                    ipAddress: ipAddress,
                    vnNow: vnNow);
            }
            else if (savedRecord.TargetType == "order" && savedRecord.OrderId.HasValue)
            {
                finalOrder = await db.TbOrders.FirstAsync(o => o.OrderId == savedRecord.OrderId.Value, cancellationToken);
                postedLedgerTx = await OrderService.PostPayPalOrderLedgerInCurrentTxAsync(
                    db,
                    finalOrder,
                    savedRecord.AmountVnd,
                    finalSnap.CaptureId!,
                    savedRecord.PayPalOrderId!,
                    note: $"PayPal Sandbox {savedRecord.PublicAttemptCode} - {PayPalOptions.FormatUsdInvariant(savedRecord.AmountUsd)} USD (@{savedRecord.ExchangeRateVndPerUsd:0})",
                    actorAccountId: actorAccountId,
                    actorUsername: actorUsername,
                    ipAddress: ipAddress,
                    vnNow: vnNow);
            }

            savedRecord.LedgerTransactionId = postedLedgerTx?.TransactionId;

            if (stillEligible)
            {
                savedRecord.LocalStatus = "Completed";
                await db.SaveChangesAsync(cancellationToken);
                if (txB != null) await txB.CommitAsync(cancellationToken);
                return PayPalCheckoutResult.Ok(savedRecord, finalBooking, finalOrder, postedLedgerTx);
            }

            // Target became ineligible while user was paying on PayPal (Rule 4 & Rule 7)
            compensationNeeded = true;
            compensationReason = eligMsg;
            savedRecord.LocalStatus = "CapturedCompensationPending";
            savedRecord.LastErrorCode = eligCode;
            savedRecord.LastErrorMessage = TruncateError(eligMsg);
            savedRecord.RefundIdempotencyKey ??= $"comp-rfd-{savedRecord.PublicAttemptCode}";
            await db.SaveChangesAsync(cancellationToken);
            if (txB != null) await txB.CommitAsync(cancellationToken);
        }

        // =========================================================================
        // PHASE C (Compensation Refund): Executed OUTSIDE SQL TX after capture proof
        // and ledger receipt row are safely committed to SQL!
        // =========================================================================
        if (compensationNeeded)
        {
            PayPalRefundResult compRefundRes;
            try
            {
                compRefundRes = await _payPalClient.RefundCaptureAsync(
                    savedRecord.PayPalCaptureId!,
                    savedRecord.AmountUsd,
                    savedRecord.CurrencyCode,
                    $"Auto-refund: {savedRecord.TargetCode} expired/settled",
                    savedRecord.RefundIdempotencyKey!,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                compRefundRes = new PayPalRefundResult(false, null, null, null, null, null, true, 504, "PAYPAL_COMP_REFUND_EXCEPTION", ex.Message);
            }

            if (compRefundRes.Success && !string.IsNullOrWhiteSpace(compRefundRes.RefundId))
            {
                var finalized = await FinalizePayPalRefundInLedgerAsync(
                    db,
                    savedRecord.PayPalTransactionId,
                    compRefundRes,
                    isCompensationAfterExpiry: true,
                    reason: compensationReason,
                    actorAccountId: actorAccountId,
                    actorUsername: actorUsername ?? "paypal_compensation",
                    ipAddress: ipAddress,
                    cancellationToken: cancellationToken);

                return PayPalCheckoutResult.Fail(
                    $"{compensationReason} Hệ thống đã tự động hoàn tiền lại qua PayPal Sandbox (Refund ID: {compRefundRes.RefundId}).",
                    409,
                    "PAYPAL_CAPTURED_AND_REFUNDED",
                    needsReconciliation: false,
                    ppTx: finalized.PayPalTransaction ?? savedRecord);
            }

            // Mark CompensationRefundRequired so worker/admin retries without losing capture evidence
            db.ChangeTracker.Clear();
            await using var txCompFail = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
            var recFail = await db.TbPayPalTransactions.FirstAsync(x => x.PayPalTransactionId == savedRecord.PayPalTransactionId, cancellationToken);
            recFail.LocalStatus = "CompensationRefundRequired";
            recFail.LastErrorCode = compRefundRes.ErrorCode ?? "PAYPAL_COMPENSATION_REFUND_FAILED";
            recFail.LastErrorMessage = TruncateError(compRefundRes.ErrorMessage ?? "Hoàn tiền tự động sau khi hết hạn giữ chỗ bị lỗi, cần đối soát hoàn tiền.");
            recFail.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (txCompFail != null) await txCompFail.CommitAsync(cancellationToken);

            return PayPalCheckoutResult.Fail(
                $"{compensationReason} Tuy nhiên hoàn tiền tự động PayPal gặp sự cố tạm thời và đã được đưa vào hàng đợi đối soát hoàn tiền.",
                409,
                "PAYPAL_COMPENSATION_REFUND_PENDING",
                needsReconciliation: true,
                ppTx: recFail);
        }

        return PayPalCheckoutResult.Ok(savedRecord, finalBooking, finalOrder, postedLedgerTx);
    }

    private async Task<PayPalCheckoutResult> FinalizePayPalRefundInLedgerAsync(
        PickleballContext db,
        int payPalTransactionId,
        PayPalRefundResult refundRes,
        bool isCompensationAfterExpiry,
        string reason,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        bool isRelational = db.Database.IsRelational();
        DateTime utcNow = DateTime.UtcNow;
        var vnNow = BookingService.GetVietnamNow();

        db.ChangeTracker.Clear();
        await using var tx = isRelational ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;

        if (isRelational)
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT PayPalTransactionId FROM tb_PayPalTransaction WITH (UPDLOCK, HOLDLOCK) WHERE PayPalTransactionId = {0}",
                payPalTransactionId);
        }

        var record = await db.TbPayPalTransactions.FirstAsync(x => x.PayPalTransactionId == payPalTransactionId, cancellationToken);
        record.PayPalRefundId = refundRes.RefundId;
        record.RefundedAtUtc ??= utcNow;
        record.UpdatedAtUtc = utcNow;
        record.LastErrorCode = null;
        record.LastErrorMessage = null;
        record.LocalStatus = isCompensationAfterExpiry ? "RefundedAfterExpiry" : "Refunded";

        TbBooking? bk = null;
        TbOrder? ord = null;
        TbPaymentTransaction? refundLedgerRow = null;

        if (record.TargetType == "booking" && record.BookingId.HasValue)
        {
            bk = await db.TbBookings.Include(b => b.Court).FirstAsync(b => b.BookingId == record.BookingId.Value, cancellationToken);
            refundLedgerRow = await BookingService.PostPayPalRefundLedgerInCurrentTxAsync(
                db,
                bk,
                record.AmountVnd,
                refundRes.RefundId!,
                note: $"{reason} (Refund {refundRes.RefundId})",
                actorAccountId: actorAccountId,
                actorUsername: actorUsername,
                ipAddress: ipAddress,
                vnNow: vnNow);
        }
        else if (record.TargetType == "order" && record.OrderId.HasValue)
        {
            ord = await db.TbOrders.FirstAsync(o => o.OrderId == record.OrderId.Value, cancellationToken);
            refundLedgerRow = await OrderService.PostPayPalOrderRefundLedgerInCurrentTxAsync(
                db,
                ord,
                record.AmountVnd,
                refundRes.RefundId!,
                note: $"{reason} (Refund {refundRes.RefundId})",
                actorAccountId: actorAccountId,
                actorUsername: actorUsername,
                ipAddress: ipAddress,
                vnNow: vnNow);
        }

        record.CompensationRefundLedgerTxId = refundLedgerRow?.TransactionId;
        await db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return PayPalCheckoutResult.Ok(record, bk, ord, refundLedgerRow);
    }

    private async Task<(bool IsEligible, string Code, string Message)> EvaluateTargetEligibilityAsync(
        PickleballContext db,
        TbPayPalTransaction ppTx,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (ppTx.TargetType == "booking" && ppTx.BookingId.HasValue)
        {
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT BookingId FROM tb_Booking WITH (UPDLOCK, HOLDLOCK) WHERE BookingId = {0}",
                    ppTx.BookingId.Value);
            }

            var booking = await db.TbBookings.FirstOrDefaultAsync(b => b.BookingId == ppTx.BookingId.Value, cancellationToken);
            if (booking == null)
            {
                return (false, "BOOKING_NOT_FOUND", "Không tìm thấy ca đặt sân.");
            }
            if (booking.Status is "cancelled" or "rejected" or "expired")
            {
                return (false, "BOOKING_INACTIVE", $"Ca đặt sân {booking.BookingCode} đã ở trạng thái '{booking.Status}'.");
            }
            if (booking.Status == "pending" && booking.HoldExpiresAtUtc.HasValue && booking.HoldExpiresAtUtc.Value <= utcNow)
            {
                return (false, "BOOKING_HOLD_EXPIRED", $"Ca đặt sân {booking.BookingCode} đã quá thời gian giữ chỗ thanh toán.");
            }

            int amountDue = booking.AmountDue > 0
                ? booking.AmountDue
                : Math.Max(0, booking.CourtFee - booking.DiscountAmount + booking.PosTotal);
            int netPaid = await BookingService.ComputeBookingLedgerNetPaidAsync(db, booking.BookingId);
            int remaining = Math.Max(0, amountDue - netPaid);
            if (remaining == 0)
            {
                return (false, "ALREADY_PAID", $"Ca đặt sân {booking.BookingCode} đã được thanh toán đủ trước đó.");
            }
            if (ppTx.AmountVnd > remaining)
            {
                return (false, "INSUFFICIENT_REMAINING", $"Số dư còn lại của ca đặt sân ({remaining:N0} đ) nhỏ hơn khoản PayPal ({ppTx.AmountVnd:N0} đ).");
            }
            return (true, string.Empty, string.Empty);
        }

        if (ppTx.TargetType == "order" && ppTx.OrderId.HasValue)
        {
            if (db.Database.IsRelational())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT OrderId FROM tb_Order WITH (UPDLOCK, HOLDLOCK) WHERE OrderId = {0}",
                    ppTx.OrderId.Value);
            }

            var order = await db.TbOrders.FirstOrDefaultAsync(o => o.OrderId == ppTx.OrderId.Value, cancellationToken);
            if (order == null)
            {
                return (false, "ORDER_NOT_FOUND", "Không tìm thấy đơn Pro Shop.");
            }
            if (order.BookingId.HasValue)
            {
                return (false, "ORDER_MERGED_TO_BOOKING", "Đơn hàng đã được gộp vào hóa đơn sân.");
            }
            if (order.Status is "cancelled" or "rejected")
            {
                return (false, "ORDER_INACTIVE", $"Đơn Pro Shop {order.Code} đã bị hủy.");
            }

            int totalDue = order.TotalAmount ?? 0;
            int netPaid = await OrderService.ComputeOrderLedgerNetPaidAsync(db, order.OrderId);
            int remaining = Math.Max(0, totalDue - netPaid);
            if (remaining == 0)
            {
                return (false, "ALREADY_PAID", $"Đơn Pro Shop {order.Code} đã được thanh toán đủ trước đó.");
            }
            if (ppTx.AmountVnd > remaining)
            {
                return (false, "INSUFFICIENT_REMAINING", $"Số dư còn lại của đơn ({remaining:N0} đ) nhỏ hơn khoản PayPal ({ppTx.AmountVnd:N0} đ).");
            }
            return (true, string.Empty, string.Empty);
        }

        return (false, "INVALID_TARGET", "Đối tượng thanh toán PayPal không hợp lệ.");
    }

    private async Task<int> ComputeTargetNetPaidAsync(PickleballContext db, TbPayPalTransaction ppTx, CancellationToken ct)
    {
        if (ppTx.TargetType == "booking" && ppTx.BookingId.HasValue)
        {
            return await BookingService.ComputeBookingLedgerNetPaidAsync(db, ppTx.BookingId.Value);
        }
        if (ppTx.TargetType == "order" && ppTx.OrderId.HasValue)
        {
            return await OrderService.ComputeOrderLedgerNetPaidAsync(db, ppTx.OrderId.Value);
        }
        return 0;
    }

    private async Task<(TbBooking? Booking, TbOrder? Order)> LoadTargetAsync(
        PickleballContext db,
        TbPayPalTransaction ppTx,
        CancellationToken cancellationToken)
    {
        TbBooking? bk = ppTx.BookingId.HasValue
            ? await db.TbBookings.Include(b => b.Court).AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == ppTx.BookingId.Value, cancellationToken)
            : null;
        TbOrder? ord = ppTx.OrderId.HasValue
            ? await db.TbOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == ppTx.OrderId.Value, cancellationToken)
            : null;
        return (bk, ord);
    }

    private async Task<bool> AuthorizeAttemptAsync(
        PickleballContext db,
        TbPayPalTransaction ppTx,
        int? actorAccountId,
        int? actorRoleId,
        string? guestAccessToken,
        IReadOnlyDictionary<string, string>? guestRecoveryTokens,
        CancellationToken cancellationToken)
    {
        if (actorRoleId is 1 or 3)
        {
            return true;
        }

        if (actorAccountId.HasValue && ppTx.ActorAccountId.HasValue && actorAccountId.Value == ppTx.ActorAccountId.Value)
        {
            return true;
        }

        if (ppTx.TargetType == "booking" && ppTx.BookingId.HasValue)
        {
            var booking = await db.TbBookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == ppTx.BookingId.Value, cancellationToken);
            if (booking == null) return false;
            return AuthorizeBookingCaller(booking, actorAccountId, actorRoleId, guestAccessToken, guestRecoveryTokens);
        }

        if (ppTx.TargetType == "order" && ppTx.OrderId.HasValue)
        {
            var order = await db.TbOrders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == ppTx.OrderId.Value, cancellationToken);
            if (order == null) return false;
            return AuthorizeOrderCaller(order, actorAccountId, actorRoleId, guestAccessToken, guestRecoveryTokens);
        }

        return false;
    }

    public bool AuthorizeBookingCaller(
        TbBooking booking,
        int? actorAccountId,
        int? actorRoleId,
        string? guestAccessToken,
        IReadOnlyDictionary<string, string>? guestRecoveryTokens)
    {
        if (actorRoleId is 1 or 3)
        {
            return true;
        }

        if (actorAccountId.HasValue && booking.AccountId.HasValue && booking.AccountId.Value == actorAccountId.Value)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(guestAccessToken) &&
            QrTokenService.VerifyGuestAccessToken(guestAccessToken.Trim(), booking.AccessTokenHash))
        {
            return true;
        }

        if (guestRecoveryTokens != null &&
            guestRecoveryTokens.TryGetValue(booking.BookingCode.Trim().ToUpperInvariant(), out var cookieToken) &&
            QrTokenService.VerifyGuestAccessToken(cookieToken, booking.AccessTokenHash))
        {
            return true;
        }

        return false;
    }

    public bool AuthorizeOrderCaller(
        TbOrder order,
        int? actorAccountId,
        int? actorRoleId,
        string? guestAccessToken,
        IReadOnlyDictionary<string, string>? guestRecoveryTokens)
    {
        if (actorRoleId is 1 or 3)
        {
            return true;
        }

        if (actorAccountId.HasValue && order.AccountId.HasValue && order.AccountId.Value == actorAccountId.Value)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(guestAccessToken) && VerifyGuestOrderProof(guestAccessToken, order))
        {
            return true;
        }

        if (guestRecoveryTokens != null &&
            !string.IsNullOrWhiteSpace(order.Code) &&
            guestRecoveryTokens.TryGetValue($"ORDER:{order.Code.Trim().ToUpperInvariant()}", out var cookieToken) &&
            VerifyGuestOrderProof(cookieToken, order))
        {
            return true;
        }

        return false;
    }

    private static bool AmountsMatch(TbPayPalTransaction ppTx, PayPalOrderSnapshot snap)
    {
        if (!snap.AmountUsd.HasValue || string.IsNullOrWhiteSpace(snap.CurrencyCode))
        {
            return false;
        }

        if (!string.Equals(snap.CurrencyCode.Trim(), ppTx.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Math.Abs(snap.AmountUsd.Value - ppTx.AmountUsd) <= 0.009m;
    }

    private static string? HashIp(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return null;
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ipAddress.Trim()));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    private static string? TruncateError(string? msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return null;
        string t = msg.Trim();
        return t.Length <= 500 ? t : t[..500];
    }
}
