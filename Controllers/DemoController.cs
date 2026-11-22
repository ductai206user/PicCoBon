using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Controllers;

public sealed class DemoController : Controller
{
    private static readonly Dictionary<string, string> RoleToDemoUsername = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user"] = "demo_member",
        ["member"] = "demo_member",
        ["staff"] = "demo_staff",
        ["admin"] = "demo_admin"
    };

    private readonly PickleballContext _context;
    private readonly IOptionsMonitor<DemoModeOptions> _demoOptions;
    private readonly ILogger<DemoController> _logger;

    public DemoController(
        PickleballContext context,
        IOptionsMonitor<DemoModeOptions> demoOptions,
        ILogger<DemoController>? logger = null)
    {
        _context = context;
        _demoOptions = demoOptions;
        _logger = logger ?? NullLogger<DemoController>.Instance;
    }

    [HttpPost("/demo/switch-role")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwitchRole([FromForm] string? role, [FromForm] string? returnUrl)
    {
        if (!_demoOptions.CurrentValue.Enabled)
        {
            return NotFound();
        }

        string normalizedRole = (role ?? "guest").Trim().ToLowerInvariant();
        if (normalizedRole == "guest" || normalizedRole == "logout")
        {
            await Function.SignOutAccountAsync(HttpContext);
            TempData["DemoNotice"] = "DEMO MODE: Đã chuyển về chế độ Khách xem trước (chưa đăng nhập).";
            return LocalRedirect(ResolveSafeReturnUrl(returnUrl, "/"));
        }

        if (!RoleToDemoUsername.TryGetValue(normalizedRole, out string? demoUsername))
        {
            return BadRequest("Unsupported demo role.");
        }

        // Only sign in strictly synthetic demo_* accounts seeded in InMemory DemoMode storage
        var account = await _context.TbAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Username == demoUsername && a.IsActive);

        if (account == null)
        {
            return NotFound("Synthetic demo account missing.");
        }

        await Function.SignInAccountAsync(HttpContext, account);
        _logger.LogInformation("DemoMode role switched to synthetic {DemoUsername} (RoleId={RoleId})",
            account.Username, account.RoleId);

        TempData["DemoNotice"] = $"DEMO MODE: Đang xem trước với vai trò \"{account.FullName}\" (tài khoản giả lập).";

        string defaultRedirect = (account.RoleId == 1 || account.RoleId == 3) ? "/Admin" : "/Account/Profile";
        if (account.RoleId == 3 && string.IsNullOrWhiteSpace(returnUrl))
        {
            defaultRedirect = "/Management";
        }

        return LocalRedirect(ResolveSafeReturnUrl(returnUrl, defaultRedirect));
    }

    private string ResolveSafeReturnUrl(string? candidate, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && Url.IsLocalUrl(candidate))
        {
            bool isPrivilegedRoute =
                candidate.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("/Management", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("/Checkin", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("/Reports", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("/Cms", StringComparison.OrdinalIgnoreCase);

            if (isPrivilegedRoute &&
                candidate.IndexOf("/Admin/Login", StringComparison.OrdinalIgnoreCase) < 0 &&
                Function._RoleId != 1 && Function._RoleId != 3)
            {
                return fallback;
            }
            return candidate;
        }
        return fallback;
    }
}
