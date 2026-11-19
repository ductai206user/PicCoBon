using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;

namespace PickleballClub.Services;

public sealed class PayPalClient : IPayPalClient
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<PayPalOptions> _optionsMonitor;
    private readonly ILogger<PayPalClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private string? _cachedTokenScope;
    private DateTime _tokenExpiresUtc = DateTime.MinValue;

    public PayPalClient(
        HttpClient http,
        IOptionsMonitor<PayPalOptions> optionsMonitor,
        ILogger<PayPalClient> logger)
    {
        _http = http;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task<PayPalCreateOrderResponse> CreateOrderAsync(
        PayPalCreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var (valid, errCode, errMsg) = opts.ValidateForCheckout();
        if (!valid)
        {
            return new PayPalCreateOrderResponse(false, null, null, null, null, 503, errCode, errMsg);
        }

        string token;
        try
        {
            token = await GetAccessTokenAsync(opts, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal OAuth2 token acquisition failed while creating order for Attempt={AttemptCode}", request.AttemptCode);
            return new PayPalCreateOrderResponse(false, null, null, null, null, 502, "PAYPAL_AUTH_FAILED",
                "Không thể xác thực kết nối tới PayPal Sandbox.");
        }

        string baseUrl = opts.BaseUrl.TrimEnd('/');
        var bodyObj = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = request.TargetCode,
                    custom_id = request.AttemptCode,
                    invoice_id = request.AttemptCode,
                    description = request.Description.Length > 120 ? request.Description[..120] : request.Description,
                    amount = new
                    {
                        currency_code = request.CurrencyCode,
                        value = PayPalOptions.FormatUsdInvariant(request.AmountUsd)
                    }
                }
            },
            application_context = new
            {
                brand_name = string.IsNullOrWhiteSpace(request.BrandName) ? "Spike Pickleball Club (Sandbox)" : request.BrandName,
                landing_page = "LOGIN",
                shipping_preference = "NO_SHIPPING",
                user_action = "PAY_NOW",
                return_url = request.ReturnUrl,
                cancel_url = request.CancelUrl
            }
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/checkout/orders");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.TryAddWithoutValidation("PayPal-Request-Id", request.RequestId);
        msg.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        msg.Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

        try
        {
            using var res = await _http.SendAsync(msg, cancellationToken);
            string raw = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                var (ppErrCode, ppErrMsg) = ExtractPayPalError(raw, "PAYPAL_CREATE_ORDER_FAILED");
                _logger.LogWarning("PayPal CreateOrder returned HTTP {Status} for Attempt={Attempt}: Code={Code}",
                    (int)res.StatusCode, request.AttemptCode, ppErrCode);
                return new PayPalCreateOrderResponse(false, null, null, null, null, (int)res.StatusCode, ppErrCode, ppErrMsg);
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string? ppOrderId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            string? status = root.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
            string? approveUrl = null;

            if (root.TryGetProperty("links", out var linksEl) && linksEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in linksEl.EnumerateArray())
                {
                    string? rel = link.TryGetProperty("rel", out var relEl) ? relEl.GetString() : null;
                    if (string.Equals(rel, "approve", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                    {
                        approveUrl = link.TryGetProperty("href", out var hrefEl) ? hrefEl.GetString() : null;
                        if (string.Equals(rel, "approve", StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(ppOrderId) || string.IsNullOrWhiteSpace(approveUrl))
            {
                return new PayPalCreateOrderResponse(false, ppOrderId, status, approveUrl, null, 502,
                    "PAYPAL_INVALID_RESPONSE", "PayPal Sandbox không trả về đầy đủ Order ID hoặc URL xác nhận.");
            }

            string summaryJson = JsonSerializer.Serialize(new
            {
                id = ppOrderId,
                status,
                amountUsd = PayPalOptions.FormatUsdInvariant(request.AmountUsd),
                currency = request.CurrencyCode
            });

            return new PayPalCreateOrderResponse(true, ppOrderId, status, approveUrl, summaryJson, (int)res.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PayPalCreateOrderResponse(false, null, null, null, null, 504, "PAYPAL_TIMEOUT",
                "Kết nối tới PayPal Sandbox bị quá thời gian khi tạo đơn thanh toán.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal CreateOrder network error for Attempt={Attempt}", request.AttemptCode);
            return new PayPalCreateOrderResponse(false, null, null, null, null, 502, "PAYPAL_NETWORK_ERROR",
                "Không thể kết nối tới máy chủ PayPal Sandbox lúc này.");
        }
    }

    public async Task<PayPalOrderSnapshot> GetOrderAsync(
        string payPalOrderId,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var (valid, errCode, errMsg) = opts.ValidateForCheckout();
        if (!valid)
        {
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                false, 503, errCode, errMsg);
        }

        string token;
        try
        {
            token = await GetAccessTokenAsync(opts, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal OAuth2 failure during GetOrder {PayPalOrderId}", payPalOrderId);
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                true, 502, "PAYPAL_AUTH_FAILED", "Không thể xác thực tới PayPal Sandbox khi đối soát đơn.");
        }

        string baseUrl = opts.BaseUrl.TrimEnd('/');
        using var msg = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var res = await _http.SendAsync(msg, cancellationToken);
            string raw = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                var (ppCode, ppMsg) = ExtractPayPalError(raw, "PAYPAL_GET_ORDER_FAILED");
                return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                    (int)res.StatusCode >= 500, (int)res.StatusCode, ppCode, ppMsg);
            }

            return ParseOrderSnapshot(raw, (int)res.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                true, 504, "PAYPAL_TIMEOUT", "Quá thời gian truy vấn trạng thái đơn PayPal Sandbox.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal GetOrder error for {PayPalOrderId}", payPalOrderId);
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                true, 502, "PAYPAL_NETWORK_ERROR", "Lỗi mạng khi truy vấn PayPal Sandbox.");
        }
    }

    public async Task<PayPalOrderSnapshot> CaptureOrderAsync(
        string payPalOrderId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var (valid, errCode, errMsg) = opts.ValidateForCheckout();
        if (!valid)
        {
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                false, 503, errCode, errMsg);
        }

        string token;
        try
        {
            token = await GetAccessTokenAsync(opts, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal OAuth2 failure during Capture {PayPalOrderId}", payPalOrderId);
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                false, 502, "PAYPAL_AUTH_FAILED", "Không thể xác thực tới PayPal Sandbox để capture giao dịch.");
        }

        string baseUrl = opts.BaseUrl.TrimEnd('/');
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/checkout/orders/{Uri.EscapeDataString(payPalOrderId)}/capture");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        msg.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        msg.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        try
        {
            using var res = await _http.SendAsync(msg, cancellationToken);
            string raw = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                var (ppCode, ppMsg) = ExtractPayPalError(raw, "PAYPAL_CAPTURE_FAILED");
                bool isAmbiguous = (int)res.StatusCode >= 500 || (int)res.StatusCode == 408 ||
                                   string.Equals(ppCode, "ORDER_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase);
                _logger.LogWarning("PayPal Capture HTTP {Status} for {OrderId}: Code={Code}",
                    (int)res.StatusCode, payPalOrderId, ppCode);
                return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                    isAmbiguous, (int)res.StatusCode, ppCode, ppMsg);
            }

            return ParseOrderSnapshot(raw, (int)res.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("PayPal Capture timed out for {PayPalOrderId}; flagging ambiguous reconciliation.", payPalOrderId);
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                true, 504, "PAYPAL_CAPTURE_TIMEOUT", "Kết nối bị gián đoạn khi xác nhận thu tiền PayPal; cần đối soát lại trạng thái.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal Capture exception for {PayPalOrderId}; flagging ambiguous reconciliation.", payPalOrderId);
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null,
                true, 502, "PAYPAL_CAPTURE_NETWORK_ERROR", "Mất kết nối sau khi gửi yêu cầu capture; cần đối soát lại với PayPal.");
        }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amountUsd,
        string currencyCode,
        string noteToPayer,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var (valid, errCode, errMsg) = opts.ValidateForCheckout();
        if (!valid)
        {
            return new PayPalRefundResult(false, null, null, null, null, null, false, 503, errCode, errMsg);
        }

        string token;
        try
        {
            token = await GetAccessTokenAsync(opts, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal OAuth2 failed while refunding Capture={CaptureId}", captureId);
            return new PayPalRefundResult(false, null, null, null, null, null, true, 502, "PAYPAL_AUTH_FAILED",
                "Không thể xác thực PayPal Sandbox khi thực hiện hoàn tiền.");
        }

        string baseUrl = opts.BaseUrl.TrimEnd('/');
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        msg.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        string cleanNote = string.IsNullOrWhiteSpace(noteToPayer)
            ? "Refund issued by Spike Pickleball Club Sandbox"
            : (noteToPayer.Length > 250 ? noteToPayer[..250] : noteToPayer);

        object bodyObj = amountUsd.HasValue
            ? new
            {
                amount = new
                {
                    currency_code = currencyCode,
                    value = PayPalOptions.FormatUsdInvariant(amountUsd.Value)
                },
                note_to_payer = cleanNote
            }
            : new
            {
                note_to_payer = cleanNote
            };

        msg.Content = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

        try
        {
            using var res = await _http.SendAsync(msg, cancellationToken);
            string raw = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                var (ppCode, ppMsg) = ExtractPayPalError(raw, "PAYPAL_REFUND_FAILED");
                return new PayPalRefundResult(false, null, null, null, null, null,
                    (int)res.StatusCode >= 500, (int)res.StatusCode, ppCode, ppMsg);
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string? refundId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            string? refundStatus = root.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
            decimal? refundedUsd = null;
            string? refundedCurrency = null;

            if (root.TryGetProperty("amount", out var amtEl))
            {
                refundedCurrency = amtEl.TryGetProperty("currency_code", out var curEl) ? curEl.GetString() : null;
                if (amtEl.TryGetProperty("value", out var valEl) &&
                    decimal.TryParse(valEl.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedUsd))
                {
                    refundedUsd = parsedUsd;
                }
            }

            string summary = JsonSerializer.Serialize(new
            {
                refundId,
                status = refundStatus,
                amountUsd = refundedUsd,
                currency = refundedCurrency
            });

            return new PayPalRefundResult(true, refundId, refundStatus, refundedUsd, refundedCurrency, summary, false, (int)res.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal Refund HTTP exception for Capture={CaptureId}", captureId);
            return new PayPalRefundResult(false, null, null, null, null, null, true, 502, "PAYPAL_REFUND_TIMEOUT",
                "Mất kết nối hoặc quá thời gian khi hoàn tiền qua PayPal Sandbox.");
        }
    }

    public async Task<bool> VerifyWebhookSignatureAsync(
        PayPalWebhookHeaders headers,
        JsonElement rawWebhookEvent,
        CancellationToken cancellationToken = default)
    {
        var opts = _optionsMonitor.CurrentValue;
        var (valid, _, _) = opts.ValidateForCheckout();
        if (!valid || string.IsNullOrWhiteSpace(opts.WebhookId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(headers.TransmissionId) ||
            string.IsNullOrWhiteSpace(headers.TransmissionTime) ||
            string.IsNullOrWhiteSpace(headers.CertUrl) ||
            string.IsNullOrWhiteSpace(headers.AuthAlgo) ||
            string.IsNullOrWhiteSpace(headers.TransmissionSig))
        {
            return false;
        }

        string token;
        try
        {
            token = await GetAccessTokenAsync(opts, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal OAuth2 failed during webhook signature verification.");
            return false;
        }

        string baseUrl = opts.BaseUrl.TrimEnd('/');
        var payload = new
        {
            auth_algo = headers.AuthAlgo,
            cert_url = headers.CertUrl,
            transmission_id = headers.TransmissionId,
            transmission_sig = headers.TransmissionSig,
            transmission_time = headers.TransmissionTime,
            webhook_id = opts.WebhookId.Trim(),
            webhook_event = rawWebhookEvent
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/notifications/verify-webhook-signature");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            using var res = await _http.SendAsync(msg, cancellationToken);
            if (!res.IsSuccessStatusCode) return false;
            string raw = await res.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(raw);
            string? verifyStatus = doc.RootElement.TryGetProperty("verification_status", out var el) ? el.GetString() : null;
            return string.Equals(verifyStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PayPal webhook verification exception.");
            return false;
        }
    }

    private async Task<string> GetAccessTokenAsync(PayPalOptions opts, CancellationToken cancellationToken)
    {
        string scopeKey = $"{opts.BaseUrl.TrimEnd('/')}|{opts.ClientId}";
        if (!string.IsNullOrEmpty(_cachedToken) &&
            string.Equals(_cachedTokenScope, scopeKey, StringComparison.Ordinal) &&
            DateTime.UtcNow < _tokenExpiresUtc)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_cachedToken) &&
                string.Equals(_cachedTokenScope, scopeKey, StringComparison.Ordinal) &&
                DateTime.UtcNow < _tokenExpiresUtc)
            {
                return _cachedToken;
            }

            string baseUrl = opts.BaseUrl.TrimEnd('/');
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opts.ClientId}:{opts.ClientSecret}"));

            using var msg = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/oauth2/token");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            msg.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var res = await _http.SendAsync(msg, cancellationToken);
            string raw = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"PayPal OAuth2 token endpoint returned HTTP {(int)res.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(raw);
            string? accessToken = doc.RootElement.TryGetProperty("access_token", out var tkEl) ? tkEl.GetString() : null;
            int expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exEl) && exEl.TryGetInt32(out int sec) ? sec : 300;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("PayPal OAuth2 response did not contain an access_token.");
            }

            _cachedToken = accessToken;
            _cachedTokenScope = scopeKey;
            _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static PayPalOrderSnapshot ParseOrderSnapshot(string raw, int httpStatus)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        string? orderId = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        string? orderStatus = root.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;

        string? payerId = null;
        string? payerEmail = null;
        if (root.TryGetProperty("payer", out var payerEl) && payerEl.ValueKind == JsonValueKind.Object)
        {
            payerId = payerEl.TryGetProperty("payer_id", out var pidEl) ? pidEl.GetString() : null;
            payerEmail = payerEl.TryGetProperty("email_address", out var peEl) ? peEl.GetString() : null;
        }

        string? captureId = null;
        string? captureStatus = null;
        decimal? amountUsd = null;
        string? currencyCode = null;

        if (root.TryGetProperty("purchase_units", out var puEl) &&
            puEl.ValueKind == JsonValueKind.Array &&
            puEl.GetArrayLength() > 0)
        {
            var firstUnit = puEl[0];
            if (firstUnit.TryGetProperty("payments", out var payEl) &&
                payEl.TryGetProperty("captures", out var capsEl) &&
                capsEl.ValueKind == JsonValueKind.Array &&
                capsEl.GetArrayLength() > 0)
            {
                var firstCap = capsEl[0];
                captureId = firstCap.TryGetProperty("id", out var capIdEl) ? capIdEl.GetString() : null;
                captureStatus = firstCap.TryGetProperty("status", out var capStEl) ? capStEl.GetString() : null;

                if (firstCap.TryGetProperty("amount", out var capAmtEl))
                {
                    currencyCode = capAmtEl.TryGetProperty("currency_code", out var cEl) ? cEl.GetString() : null;
                    if (capAmtEl.TryGetProperty("value", out var vEl) &&
                        decimal.TryParse(vEl.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decVal))
                    {
                        amountUsd = decVal;
                    }
                }
            }
            else if (firstUnit.TryGetProperty("amount", out var unitAmtEl))
            {
                currencyCode = unitAmtEl.TryGetProperty("currency_code", out var cEl) ? cEl.GetString() : null;
                if (unitAmtEl.TryGetProperty("value", out var vEl) &&
                    decimal.TryParse(vEl.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decVal))
                {
                    amountUsd = decVal;
                }
            }
        }

        string summaryJson = JsonSerializer.Serialize(new
        {
            id = orderId,
            status = orderStatus,
            captureId,
            captureStatus,
            amountUsd = amountUsd.HasValue ? PayPalOptions.FormatUsdInvariant(amountUsd.Value) : null,
            currency = currencyCode,
            payerId
        });

        return new PayPalOrderSnapshot(
            true,
            orderId,
            orderStatus,
            captureId,
            captureStatus,
            amountUsd,
            currencyCode,
            payerId,
            payerEmail,
            summaryJson,
            false,
            httpStatus);
    }

    private static (string Code, string Message) ExtractPayPalError(string raw, string fallbackCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string code = fallbackCode;
            string message = "PayPal Sandbox từ chối xử lý yêu cầu.";

            if (root.TryGetProperty("details", out var detailsEl) &&
                detailsEl.ValueKind == JsonValueKind.Array &&
                detailsEl.GetArrayLength() > 0)
            {
                var first = detailsEl[0];
                if (first.TryGetProperty("issue", out var issueEl) && !string.IsNullOrWhiteSpace(issueEl.GetString()))
                    code = issueEl.GetString()!;
                if (first.TryGetProperty("description", out var descEl) && !string.IsNullOrWhiteSpace(descEl.GetString()))
                    message = descEl.GetString()!;
                return (code, message);
            }

            if (root.TryGetProperty("name", out var nameEl) && !string.IsNullOrWhiteSpace(nameEl.GetString()))
                code = nameEl.GetString()!;
            if (root.TryGetProperty("message", out var msgEl) && !string.IsNullOrWhiteSpace(msgEl.GetString()))
                message = msgEl.GetString()!;
            return (code, message);
        }
        catch
        {
            return (fallbackCode, "PayPal Sandbox trả về lỗi không xác định.");
        }
    }
}
