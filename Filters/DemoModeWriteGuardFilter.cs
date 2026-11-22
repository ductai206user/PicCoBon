using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;

namespace PickleballClub.Filters;

public sealed class DemoModeWriteGuardFilter : IAsyncActionFilter, IOrderedFilter
{
    private static readonly HashSet<string> AllowedPostPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/demo/switch-role",
        "/api/bookings/quote",
        "/api/assistant/chat",
        "/api/assistant/reset",
        "/api/auth/logout"
    };

    private readonly IOptionsMonitor<DemoModeOptions> _demoOptions;
    private readonly ITempDataDictionaryFactory _tempDataFactory;

    public int Order => -2000;

    public DemoModeWriteGuardFilter(
        IOptionsMonitor<DemoModeOptions> demoOptions,
        ITempDataDictionaryFactory tempDataFactory)
    {
        _demoOptions = demoOptions;
        _tempDataFactory = tempDataFactory;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!_demoOptions.CurrentValue.Enabled)
        {
            await next();
            return;
        }

        string method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) ||
            HttpMethods.IsHead(method) ||
            HttpMethods.IsOptions(method) ||
            HttpMethods.IsTrace(method))
        {
            await next();
            return;
        }

        string path = (context.HttpContext.Request.Path.Value ?? "/").TrimEnd('/');
        if (string.IsNullOrEmpty(path)) path = "/";

        if (AllowedPostPaths.Contains(path))
        {
            await next();
            return;
        }

        string actionDesc = DescribeBlockedOperation(path);
        string message = $"DEMO MODE: {actionDesc} Không ghi dữ liệu vào máy chủ hoặc CSDL thật.";

        bool wantsJson = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                         context.HttpContext.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(context.HttpContext.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (wantsJson)
        {
            context.Result = new JsonResult(new
            {
                ok = false,
                demoMode = true,
                code = "DEMO_MODE_READONLY",
                error = message
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        var tempData = _tempDataFactory.GetTempData(context.HttpContext);
        tempData["DemoNotice"] = message;

        if (path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Account/Register", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Admin/Login", StringComparison.OrdinalIgnoreCase))
        {
            tempData["AuthError"] = message;
        }

        string? referer = context.HttpContext.Request.Headers.Referer.ToString();
        string redirectTarget = "/";
        if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
        {
            string local = refUri.PathAndQuery;
            if (!string.IsNullOrWhiteSpace(local) && local.StartsWith("/", StringComparison.Ordinal))
            {
                redirectTarget = local;
            }
        }
        else if (path.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase))
        {
            redirectTarget = "/Admin";
        }

        context.Result = new RedirectResult(redirectTarget);
    }

    private static string DescribeBlockedOperation(string path)
    {
        if (path.Contains("/paypal", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/payments", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/refunds", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/webhooks", StringComparison.OrdinalIgnoreCase))
        {
            return "Thanh toán PayPal, thu tiền và hoàn tiền thật đã bị tắt trong bản xem trước.";
        }

        if (path.Contains("/checkin", StringComparison.OrdinalIgnoreCase))
        {
            return "Thao tác Check-in mở sân thật đã bị vô hiệu hóa trong bản xem trước.";
        }

        if (path.StartsWith("/api/bookings", StringComparison.OrdinalIgnoreCase))
        {
            return "Đặt sân và hủy vé thật đã bị vô hiệu hóa (chỉ xem trước giao diện & báo giá).";
        }

        if (path.StartsWith("/api/orders", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/orders/", StringComparison.OrdinalIgnoreCase))
        {
            return "Tạo và xử lý đơn hàng Pro Shop thật đã bị vô hiệu hóa trong bản xem trước.";
        }

        if (path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Admin/Login", StringComparison.OrdinalIgnoreCase))
        {
            return "Xác thực và thay đổi tài khoản thật đã tắt — hãy chọn nút vai trò DEMO để xem trước.";
        }

        return "Thao tác thay đổi dữ liệu quản trị đã bị tắt ở chế độ Demo chỉ đọc.";
    }
}
