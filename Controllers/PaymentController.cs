using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Controllers;

public sealed class PaymentController : Controller
{
    private readonly PickleballContext _context;
    private readonly PayPalPaymentService _payPalPaymentService;
    private readonly IOptionsMonitor<PayPalOptions> _payPalOptions;
    private readonly QrTokenService _qrTokenService;
    private readonly IAntiforgery _antiforgery;
    private readonly IDataProtector _guestRecoveryProtector;

    public PaymentController(
        PickleballContext context,
        PayPalPaymentService payPalPaymentService,
        IOptionsMonitor<PayPalOptions> payPalOptions,
        QrTokenService qrTokenService,
        IAntiforgery antiforgery,
        IDataProtectionProvider dataProtectionProvider)
    {
        _context = context;
        _payPalPaymentService = payPalPaymentService;
        _payPalOptions = payPalOptions;
        _qrTokenService = qrTokenService;
        _antiforgery = antiforgery;
        _guestRecoveryProtector = GuestRecoveryCookieHelper.CreateProtector(dataProtectionProvider);
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    // =========================================================================
    // MVC RETURN / CANCEL VIEWS — Strictly Read-Only (Zero DB mutations on GET)
    // =========================================================================

    [HttpGet("/Payment/PayPalReturn")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult PayPalReturn([FromQuery] string? state, [FromQuery] string? token, [FromQuery] string? PayerID)
    {
        _antiforgery.GetAndStoreTokens(HttpContext);

        bool validState = _payPalPaymentService.TryUnprotectState(state, out var payload) && payload != null;
        ViewData["Title"] = "Xác nhận thanh toán PayPal Sandbox — Spike OS";
        ViewBag.StateToken = state ?? string.Empty;
        ViewBag.PayPalOrderId = token ?? string.Empty;
        ViewBag.PayPalPayerId = PayerID ?? string.Empty;
        ViewBag.IsValidState = validState;
        ViewBag.AttemptCode = payload?.AttemptCode ?? string.Empty;
        ViewBag.TargetType = payload?.TargetType ?? string.Empty;
        ViewBag.TargetCode = payload?.TargetCode ?? string.Empty;
        ViewBag.VndPerUsd = _payPalOptions.CurrentValue.VndPerUsd;
        ViewBag.PayPalEnvironment = _payPalOptions.CurrentValue.Environment ?? "Sandbox";
        return View();
    }

    [HttpGet("/Payment/PayPalCancel")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult PayPalCancel([FromQuery] string? state, [FromQuery] string? token)
    {
        _antiforgery.GetAndStoreTokens(HttpContext);

        bool validState = _payPalPaymentService.TryUnprotectState(state, out var payload) && payload != null;
        ViewData["Title"] = "Đã hủy thanh toán PayPal Sandbox — Spike OS";
        ViewBag.StateToken = state ?? string.Empty;
        ViewBag.PayPalOrderId = token ?? string.Empty;
        ViewBag.IsValidState = validState;
        ViewBag.AttemptCode = payload?.AttemptCode ?? string.Empty;
        ViewBag.TargetType = payload?.TargetType ?? string.Empty;
        ViewBag.TargetCode = payload?.TargetCode ?? string.Empty;
        ViewBag.PayPalEnvironment = _payPalOptions.CurrentValue.Environment ?? "Sandbox";
        return View();
    }

    // =========================================================================
    // PUBLIC & AUTHENTICATED CHECKOUT APIs
    // =========================================================================

    [HttpGet("/api/payments/paypal/config")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> GetPayPalPublicConfig(CancellationToken cancellationToken)
    {
        var opts = _payPalOptions.CurrentValue;
        var check = await _payPalPaymentService.ValidateCheckoutReadinessAsync(_context, cancellationToken);
        return Ok(new
        {
            ok = true,
            enabled = check.IsReady,
            environment = opts.Environment,
            currency = opts.Currency,
            vndPerUsd = opts.VndPerUsd,
            brandName = opts.BrandName,
            disabledCode = check.IsReady ? null : check.ErrorCode,
            disabledReason = check.IsReady ? null : check.ErrorMessage
        });
    }

    [ValidateAntiForgeryToken]
    [EnableRateLimiting("PayPalLimiter")]
    [HttpPost("/api/payments/paypal/create-order")]
    public async Task<IActionResult> CreatePayPalOrderApi([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        string targetType = payload.TryGetProperty("targetType", out var ttEl) ? (ttEl.GetString() ?? "booking") : "booking";
        string targetCode = payload.TryGetProperty("targetCode", out var tcEl) ? (tcEl.GetString() ?? string.Empty) : string.Empty;
        string? idempotencyKey = payload.TryGetProperty("idempotencyKey", out var ikEl) ? ikEl.GetString() : null;
        string? guestAccessToken = payload.TryGetProperty("guestAccessToken", out var gtEl)
            ? gtEl.GetString()
            : (payload.TryGetProperty("bookingAccessToken", out var batEl)
                ? batEl.GetString()
                : Request.Headers["X-Booking-Access-Token"].FirstOrDefault());

        var guestTokens = GuestRecoveryCookieHelper.ReadActiveTokenMap(Request, _guestRecoveryProtector);
        string returnBaseUrl = $"{Request.Scheme}://{Request.Host}";

        var result = await _payPalPaymentService.CreateCheckoutAsync(_context, new PayPalCreateCheckoutInput
        {
            TargetType = targetType,
            TargetCode = targetCode,
            IdempotencyKey = idempotencyKey,
            ReturnBaseUrl = returnBaseUrl,
            ActorAccountId = Function.IsLogin() ? Function._AccountId : null,
            ActorRoleId = Function.IsLogin() ? Function._RoleId : null,
            ActorUsername = Function.IsLogin() ? Function._Username : null,
            GuestAccessToken = guestAccessToken,
            GuestRecoveryTokens = guestTokens,
            IpAddress = ClientIp
        }, cancellationToken);

        return MapPayPalResultToActionResult(result, guestAccessToken, guestTokens);
    }

    [ValidateAntiForgeryToken]
    [EnableRateLimiting("PayPalLimiter")]
    [HttpPost("/api/payments/paypal/capture")]
    public async Task<IActionResult> CapturePayPalOrderApi([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        string stateToken = payload.TryGetProperty("state", out var stEl) ? (stEl.GetString() ?? string.Empty) : string.Empty;
        string? payPalOrderId = payload.TryGetProperty("payPalOrderId", out var oidEl)
            ? oidEl.GetString()
            : (payload.TryGetProperty("token", out var tokEl) ? tokEl.GetString() : null);
        string? guestAccessToken = payload.TryGetProperty("guestAccessToken", out var gtEl)
            ? gtEl.GetString()
            : Request.Headers["X-Booking-Access-Token"].FirstOrDefault();

        var guestTokens = GuestRecoveryCookieHelper.ReadActiveTokenMap(Request, _guestRecoveryProtector);

        var result = await _payPalPaymentService.CaptureCheckoutAsync(_context, new PayPalCheckoutActionInput
        {
            StateToken = stateToken,
            PayPalOrderId = payPalOrderId,
            ActorAccountId = Function.IsLogin() ? Function._AccountId : null,
            ActorRoleId = Function.IsLogin() ? Function._RoleId : null,
            ActorUsername = Function.IsLogin() ? Function._Username : null,
            GuestAccessToken = guestAccessToken,
            GuestRecoveryTokens = guestTokens,
            IpAddress = ClientIp
        }, cancellationToken);

        return MapPayPalResultToActionResult(result, guestAccessToken, guestTokens);
    }

    [ValidateAntiForgeryToken]
    [EnableRateLimiting("PayPalLimiter")]
    [HttpPost("/api/payments/paypal/cancel")]
    public async Task<IActionResult> CancelPayPalOrderApi([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        string stateToken = payload.TryGetProperty("state", out var stEl) ? (stEl.GetString() ?? string.Empty) : string.Empty;
        string? payPalOrderId = payload.TryGetProperty("payPalOrderId", out var oidEl)
            ? oidEl.GetString()
            : (payload.TryGetProperty("token", out var tokEl) ? tokEl.GetString() : null);
        string? guestAccessToken = payload.TryGetProperty("guestAccessToken", out var gtEl)
            ? gtEl.GetString()
            : Request.Headers["X-Booking-Access-Token"].FirstOrDefault();

        var guestTokens = GuestRecoveryCookieHelper.ReadActiveTokenMap(Request, _guestRecoveryProtector);

        var result = await _payPalPaymentService.CancelCheckoutAsync(_context, new PayPalCheckoutActionInput
        {
            StateToken = stateToken,
            PayPalOrderId = payPalOrderId,
            ActorAccountId = Function.IsLogin() ? Function._AccountId : null,
            ActorRoleId = Function.IsLogin() ? Function._RoleId : null,
            ActorUsername = Function.IsLogin() ? Function._Username : null,
            GuestAccessToken = guestAccessToken,
            GuestRecoveryTokens = guestTokens,
            IpAddress = ClientIp
        }, cancellationToken);

        return MapPayPalResultToActionResult(result, guestAccessToken, guestTokens);
    }

    [ValidateAntiForgeryToken]
    [EnableRateLimiting("PayPalLimiter")]
    [HttpPost("/api/payments/paypal/reconcile")]
    public async Task<IActionResult> ReconcilePayPalOrderApi([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        string? attemptCode = payload.TryGetProperty("attemptCode", out var acEl) ? acEl.GetString() : null;
        string? stateToken = payload.TryGetProperty("state", out var stEl) ? stEl.GetString() : null;
        string? guestAccessToken = payload.TryGetProperty("guestAccessToken", out var gtEl)
            ? gtEl.GetString()
            : Request.Headers["X-Booking-Access-Token"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(attemptCode) &&
            !string.IsNullOrWhiteSpace(stateToken) &&
            _payPalPaymentService.TryUnprotectState(stateToken, out var parsed) &&
            parsed != null)
        {
            attemptCode = parsed.AttemptCode;
        }

        if (string.IsNullOrWhiteSpace(attemptCode))
        {
            return BadRequest(new { ok = false, code = "ATTEMPT_CODE_REQUIRED", error = "Thiếu mã phiên giao dịch PayPal cần đối soát." });
        }

        var guestTokens = GuestRecoveryCookieHelper.ReadActiveTokenMap(Request, _guestRecoveryProtector);

        var result = await _payPalPaymentService.ReconcileByAttemptCodeAsync(
            _context,
            attemptCode.Trim().ToUpperInvariant(),
            Function.IsLogin() ? Function._AccountId : null,
            Function.IsLogin() ? Function._RoleId : null,
            Function.IsLogin() ? Function._Username : null,
            guestAccessToken,
            guestTokens,
            ClientIp,
            requireCallerAuth: true,
            cancellationToken: cancellationToken);

        return MapPayPalResultToActionResult(result, guestAccessToken, guestTokens);
    }

    // =========================================================================
    // PAYPAL WEBHOOK RECEIVER — Verifies PayPal Webhook Signature (NO CSRF)
    // =========================================================================

    [HttpPost("/api/webhooks/paypal")]
    public async Task<IActionResult> ReceivePayPalWebhook(CancellationToken cancellationToken)
    {
        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }

        var headers = new PayPalWebhookHeaders(
            TransmissionId: Request.Headers["PAYPAL-TRANSMISSION-ID"].FirstOrDefault() ?? string.Empty,
            TransmissionTime: Request.Headers["PAYPAL-TRANSMISSION-TIME"].FirstOrDefault() ?? string.Empty,
            CertUrl: Request.Headers["PAYPAL-CERT-URL"].FirstOrDefault() ?? string.Empty,
            AuthAlgo: Request.Headers["PAYPAL-AUTH-ALGO"].FirstOrDefault() ?? string.Empty,
            TransmissionSig: Request.Headers["PAYPAL-TRANSMISSION-SIG"].FirstOrDefault() ?? string.Empty);

        var (ok, httpStatus, message) = await _payPalPaymentService.ProcessWebhookAsync(_context, headers, rawBody, cancellationToken);
        return StatusCode(httpStatus, new { ok, message });
    }

    // =========================================================================
    // ADMIN / STAFF RECONCILIATION & REFUND APIS
    // =========================================================================

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("/api/admin/paypal/transactions")]
    public async Task<IActionResult> GetAdminPayPalTransactions(
        [FromQuery] string? status = null,
        [FromQuery] bool needsReconciliation = false,
        [FromQuery] int limit = 60,
        CancellationToken cancellationToken = default)
    {
        if (!await PayPalPaymentService.IsPayPalSchemaAvailableAsync(_context, cancellationToken))
        {
            return Ok(new
            {
                ok = true,
                schemaNotMigrated = true,
                transactions = Array.Empty<object>()
            });
        }

        int clampedLimit = Math.Clamp(limit, 1, 200);
        var query = _context.TbPayPalTransactions.AsNoTracking().AsQueryable();

        if (needsReconciliation)
        {
            query = query.Where(x =>
                x.LocalStatus == "AmbiguousTimeout" ||
                x.LocalStatus == "CapturedCompensationPending" ||
                x.LocalStatus == "CompensationRefundRequired" ||
                x.LocalStatus == "Capturing" ||
                x.LocalStatus == "AwaitingApproval");
        }
        else if (!string.IsNullOrWhiteSpace(status))
        {
            string s = status.Trim();
            query = query.Where(x => x.LocalStatus == s);
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(clampedLimit)
            .Select(x => new
            {
                attemptCode = x.PublicAttemptCode,
                targetType = x.TargetType,
                targetCode = x.TargetCode,
                localStatus = x.LocalStatus,
                payPalOrderId = x.PayPalOrderId,
                payPalCaptureId = x.PayPalCaptureId,
                payPalRefundId = x.PayPalRefundId,
                payPalStatus = x.PayPalStatus,
                amountVnd = x.AmountVnd,
                amountUsd = x.AmountUsd,
                currencyCode = x.CurrencyCode,
                exchangeRateVndPerUsd = x.ExchangeRateVndPerUsd,
                ledgerTransactionId = x.LedgerTransactionId,
                compensationRefundLedgerTxId = x.CompensationRefundLedgerTxId,
                createdBy = x.CreatedBy,
                createdAtUtc = x.CreatedAtUtc.ToString("O"),
                capturedAtUtc = x.CapturedAtUtc.HasValue ? x.CapturedAtUtc.Value.ToString("O") : null,
                refundedAtUtc = x.RefundedAtUtc.HasValue ? x.RefundedAtUtc.Value.ToString("O") : null,
                lastErrorCode = x.LastErrorCode,
                lastErrorMessage = x.LastErrorMessage
            })
            .ToListAsync(cancellationToken);

        return Ok(new { ok = true, transactions = rows });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/paypal/{attemptCode}/reconcile")]
    public async Task<IActionResult> AdminReconcilePayPalApi(string attemptCode, CancellationToken cancellationToken)
    {
        var result = await _payPalPaymentService.ReconcileByAttemptCodeAsync(
            _context,
            attemptCode.Trim().ToUpperInvariant(),
            Function._AccountId,
            Function._RoleId,
            Function._Username,
            guestAccessToken: null,
            guestRecoveryTokens: null,
            ipAddress: ClientIp,
            requireCallerAuth: false,
            cancellationToken: cancellationToken);

        return MapPayPalResultToActionResult(result, null, null);
    }

    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/paypal/{attemptCode}/refund")]
    public async Task<IActionResult> AdminRefundPayPalApi(
        string attemptCode,
        [FromBody] JsonElement payload,
        CancellationToken cancellationToken)
    {
        string? reason = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("reason", out var rEl)
            ? rEl.GetString()
            : null;
        string? idempotencyKey = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("idempotencyKey", out var ikEl)
            ? ikEl.GetString()
            : null;

        var result = await _payPalPaymentService.RefundCapturedPaymentAsync(
            _context,
            attemptCode.Trim().ToUpperInvariant(),
            reason,
            idempotencyKey,
            Function._AccountId,
            Function._Username ?? "admin",
            ClientIp,
            cancellationToken);

        return MapPayPalResultToActionResult(result, null, null);
    }

    private IActionResult MapPayPalResultToActionResult(
        PayPalCheckoutResult result,
        string? explicitGuestToken,
        IReadOnlyDictionary<string, string>? guestRecoveryTokens)
    {
        object? bookingDto = null;
        if (result.Booking != null)
        {
            string? guestTokenForUrl = explicitGuestToken;
            if (string.IsNullOrWhiteSpace(guestTokenForUrl) &&
                guestRecoveryTokens != null &&
                guestRecoveryTokens.TryGetValue(result.Booking.BookingCode.Trim().ToUpperInvariant(), out var recovered))
            {
                guestTokenForUrl = recovered;
            }
            bookingDto = ApiDbController.BuildOwnerOrStaffBookingDto(result.Booking, _qrTokenService, guestTokenForUrl, embedQrSvg: true);
        }

        object? orderDto = result.Order != null ? ApiDbController.BuildOrderDto(result.Order) : null;

        var body = new
        {
            ok = result.Success,
            code = result.ErrorCode,
            error = result.ErrorMessage,
            deduplicated = result.IsIdempotentReplay,
            needsReconciliation = result.NeedsReconciliation,
            attemptCode = result.AttemptCode,
            targetType = result.TargetType,
            targetCode = result.TargetCode,
            localStatus = result.LocalStatus,
            payPalOrderId = result.PayPalOrderId,
            payPalCaptureId = result.PayPalCaptureId,
            payPalRefundId = result.PayPalRefundId,
            payPalStatus = result.PayPalStatus,
            approveUrl = result.ApproveUrl,
            amountVnd = result.AmountVnd,
            amountUsd = result.AmountUsd,
            currencyCode = result.CurrencyCode,
            exchangeRateVndPerUsd = result.ExchangeRateVndPerUsd,
            environment = _payPalOptions.CurrentValue.Environment ?? "Sandbox",
            booking = bookingDto,
            order = orderDto
        };

        return StatusCode(result.StatusCode, body);
    }
}
