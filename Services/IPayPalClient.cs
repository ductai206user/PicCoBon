using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PickleballClub.Services;

public sealed record PayPalCreateOrderRequest(
    string AttemptCode,
    string TargetCode,
    string Description,
    decimal AmountUsd,
    string CurrencyCode,
    string ReturnUrl,
    string CancelUrl,
    string BrandName,
    string RequestId);

public sealed record PayPalCreateOrderResponse(
    bool Success,
    string? PayPalOrderId,
    string? PayPalStatus,
    string? ApproveUrl,
    string? SanitizedSummaryJson,
    int HttpStatusCode = 200,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PayPalOrderSnapshot(
    bool Success,
    string? PayPalOrderId,
    string? OrderStatus,
    string? CaptureId,
    string? CaptureStatus,
    decimal? AmountUsd,
    string? CurrencyCode,
    string? PayerId,
    string? PayerEmail,
    string? SanitizedSummaryJson,
    bool IsAmbiguousTimeout = false,
    int HttpStatusCode = 200,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PayPalRefundResult(
    bool Success,
    string? RefundId,
    string? RefundStatus,
    decimal? AmountUsd,
    string? CurrencyCode,
    string? SanitizedSummaryJson,
    bool IsAmbiguousTimeout = false,
    int HttpStatusCode = 200,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PayPalWebhookHeaders(
    string TransmissionId,
    string TransmissionTime,
    string CertUrl,
    string AuthAlgo,
    string TransmissionSig);

public interface IPayPalClient
{
    Task<PayPalCreateOrderResponse> CreateOrderAsync(
        PayPalCreateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<PayPalOrderSnapshot> GetOrderAsync(
        string payPalOrderId,
        CancellationToken cancellationToken = default);

    Task<PayPalOrderSnapshot> CaptureOrderAsync(
        string payPalOrderId,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amountUsd,
        string currencyCode,
        string noteToPayer,
        string requestId,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyWebhookSignatureAsync(
        PayPalWebhookHeaders headers,
        JsonElement rawWebhookEvent,
        CancellationToken cancellationToken = default);
}
