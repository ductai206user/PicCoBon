using System;
using System.Globalization;

namespace PickleballClub.Configuration;

public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";
    public const string OfficialSandboxBaseUrl = "https://api-m.sandbox.paypal.com";

    public string Environment { get; set; } = "Sandbox";
    public string BaseUrl { get; set; } = OfficialSandboxBaseUrl;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string WebhookId { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public decimal VndPerUsd { get; set; }
    public string BrandName { get; set; } = "Spike Pickleball Club (Sandbox)";

    /// <summary>
    /// Validates Sandbox-only safety constraints, API credentials, and positive FX rate.
    /// Never permits switching to Live PayPal endpoints.
    /// </summary>
    public (bool IsValid, string ErrorCode, string ErrorMessage) ValidateForCheckout()
    {
        if (!string.Equals((Environment ?? string.Empty).Trim(), "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "PAYPAL_LIVE_DISABLED",
                "Cổng PayPal chỉ được phép hoạt động ở chế độ Sandbox thử nghiệm.");
        }

        string cleanBase = (BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!string.Equals(cleanBase, OfficialSandboxBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "PAYPAL_INVALID_HOST",
                "BaseUrl PayPal bắt buộc trỏ về Sandbox API chính thức (https://api-m.sandbox.paypal.com).");
        }

        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            return (false, "PAYPAL_CREDENTIALS_MISSING",
                "Chưa cấu hình khóa xác thực PayPal Sandbox (ClientId / ClientSecret). Vui lòng thiết lập qua User Secrets hoặc biến môi trường.");
        }

        if (!string.Equals((Currency ?? string.Empty).Trim(), "USD", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "PAYPAL_UNSUPPORTED_CURRENCY",
                "PayPal Sandbox hiện chỉ hỗ trợ thanh toán đơn vị USD cho quy đổi từ VNĐ.");
        }

        if (VndPerUsd <= 0m)
        {
            return (false, "PAYPAL_EXCHANGE_RATE_MISSING",
                "Chưa cấu hình tỷ giá quy đổi VNĐ sang USD hợp lệ (PayPal:VndPerUsd > 0).");
        }

        return (true, string.Empty, string.Empty);
    }

    public decimal ConvertVndToUsd(int amountVnd)
    {
        if (amountVnd <= 0 || VndPerUsd <= 0m)
        {
            return 0m;
        }

        decimal usd = Math.Round(amountVnd / VndPerUsd, 2, MidpointRounding.AwayFromZero);
        return usd < 0.01m ? 0.01m : usd;
    }

    public static string FormatUsdInvariant(decimal amountUsd) =>
        amountUsd.ToString("0.00", CultureInfo.InvariantCulture);
}
