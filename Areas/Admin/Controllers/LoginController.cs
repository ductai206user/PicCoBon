using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PickleballClub.Logging;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers;

[Area("Admin")]
public class LoginController : Controller
{
    private readonly PickleballContext _context;
    private readonly PasswordService _passwordService;
    private readonly ILogger<LoginController> _logger;

    public LoginController(PickleballContext context, PasswordService passwordService, ILogger<LoginController>? logger = null)
    {
        _context = context;
        _passwordService = passwordService;
        _logger = logger ?? NullLogger<LoginController>.Instance;
    }

    public IActionResult Index()
    {
        if (Function.IsStaffOrAdmin())
        {
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> Index(TbAccount account)
    {
        if (account == null || string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrEmpty(account.Password))
        {
            TempData["AuthError"] = "Vui lòng nhập đầy đủ tài khoản và mật khẩu quản trị!";
            return RedirectToAction("Index", "Login", new { area = "Admin" });
        }

        string inputUser = account.Username.Trim();
        string normPhone = BookingService.NormalizePhone(inputUser);
        string rawPass = account.Password;

        var check = await _context.TbAccounts.FirstOrDefaultAsync(m =>
            m.IsActive &&
            (m.Username == inputUser || m.Email == inputUser || m.Phone == inputUser || (!string.IsNullOrEmpty(normPhone) && m.Phone == normPhone)));

        if (check == null)
        {
            _logger.LogWarning("Admin login failed for Identifier={Identifier}: Reason=AccountNotFound", CorrelationContext.MaskIdentifier(inputUser));
            TempData["AuthError"] = "Tài khoản hoặc mật khẩu không chính xác!";
            return RedirectToAction("Index", "Login", new { area = "Admin" });
        }

        var verify = _passwordService.VerifyPassword(check, check.Password, rawPass, out string? upgradedHash);
        if (verify == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Admin login failed for AccountId={AccountId}: Reason=InvalidPassword", check.AccountId);
            TempData["AuthError"] = "Tài khoản hoặc mật khẩu không chính xác!";
            return RedirectToAction("Index", "Login", new { area = "Admin" });
        }

        if (check.RoleId != 1 && check.RoleId != 3)
        {
            _logger.LogWarning("Admin login denied for AccountId={AccountId}: Reason=InsufficientRole ({RoleId})", check.AccountId, check.RoleId);
            TempData["AuthError"] = "Tài khoản này không có quyền truy cập khu vực Quản trị / Điều hành!";
            return RedirectToAction("Index", "Login", new { area = "Admin" });
        }

        if (verify == PasswordVerificationResult.SuccessRehashNeeded && !string.IsNullOrEmpty(upgradedHash))
        {
            check.Password = upgradedHash;
        }

        if (string.IsNullOrEmpty(check.SecurityStamp))
        {
            check.SecurityStamp = Function.GenerateSecurityStamp();
        }

        check.LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");
        await _context.SaveChangesAsync();

        await Function.SignInAccountAsync(HttpContext, check);
        _logger.LogInformation("Admin/Staff signed in (AccountId={AccountId}, RoleId={RoleId})", check.AccountId, check.RoleId);

        return RedirectToAction("Index", "Home", new { area = "Admin" });
    }
}
