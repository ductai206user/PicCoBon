using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PickleballClub.Configuration;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PickleballClub.Tests;

public class ProfileAvatarTests : IAsyncLifetime
{
    private const string MasterConnectionString =
        "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private readonly string _dbName = "PickleballClub_AvatarTest_" + Guid.NewGuid().ToString("N")[..8];
    private string ConnectionString =>
        $"Server=.\\SQLEXPRESS;Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private readonly string _tempWebRoot = Path.Combine(Path.GetTempPath(), "spike_avatar_test_wwwroot_" + Guid.NewGuid().ToString("N")[..8]);
    private WebApplicationFactory<Program>? _factory;
    private int _memberId;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempWebRoot);
        string appRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseWebRoot(_tempWebRoot);
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddJsonFile(Path.Combine(appRoot, "appsettings.Development.json"), optional: false);
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                    ["Security:QrSigningKey"] = "AvatarTest-HMAC-SecretKey-2026-SpikePickleball-32Chars!",
                    ["BackgroundWorkers:Enabled"] = "false",
                    ["RateLimiting:AuthPermitLimit"] = "1000",
                    ["PayPal:Environment"] = "Sandbox",
                    ["PayPal:BaseUrl"] = PayPalOptions.OfficialSandboxBaseUrl,
                    ["PayPal:ClientId"] = "sbx-avatar-test",
                    ["PayPal:ClientSecret"] = "sbx-avatar-secret",
                    ["PayPal:VndPerUsd"] = "26000"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<PickleballContext>>();
                services.AddDbContext<PickleballContext>(opts => opts.UseSqlServer(ConnectionString));
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var pwd = scope.ServiceProvider.GetRequiredService<PasswordService>();
        await db.Database.MigrateAsync();

        var member = new TbAccount
        {
            Username = "avatar_member",
            FullName = "Avatar Test Athlete",
            Phone = "0907778899",
            Email = "avatar@spike.test",
            RoleId = 2,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        member.Password = pwd.HashPassword(member, "AvatarPass!2026");

        var admin = new TbAccount
        {
            Username = "avatar_admin",
            FullName = "Quan Tri Vien",
            Phone = "0907779900",
            Email = "avataradmin@spike.test",
            RoleId = 1,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        admin.Password = pwd.HashPassword(admin, "AdminPass!2026");

        db.TbAccounts.AddRange(member, admin);
        await db.SaveChangesAsync();
        _memberId = member.AccountId;
    }

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }
        try
        {
            if (Directory.Exists(_tempWebRoot))
            {
                Directory.Delete(_tempWebRoot, recursive: true);
            }
        }
        catch { }

        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(MasterConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
IF DB_ID(N'{_dbName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{_dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{_dbName}];
END";
        await cmd.ExecuteNonQueryAsync();
    }

    private HttpClient CreateCookieClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private async Task LoginUserAsync(HttpClient client, string identifier, string password)
    {
        var csrfRes = await client.GetAsync("/api/security/csrf");
        csrfRes.EnsureSuccessStatusCode();
        using var csrfDoc = JsonDocument.Parse(await csrfRes.Content.ReadAsStringAsync());
        string csrf = csrfDoc.RootElement.GetProperty("token").GetString()!;

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                identifier,
                password
            }), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("RequestVerificationToken", csrf);
        var loginRes = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
    }

    private Task LoginMemberAsync(HttpClient client) => LoginUserAsync(client, "avatar_member", "AvatarPass!2026");

    private static string ExtractFormCsrfToken(string html)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Could not locate __RequestVerificationToken hidden input in Profile page HTML.");
        return match.Groups[1].Value;
    }

    private static byte[] CreateTestPngBytes(int width = 320, int height = 240)
    {
        using var img = new Image<Rgba32>(width, height, new Rgba32(46, 139, 87, 255));
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static byte[] CreateTestJpegBytes(int width = 300, int height = 300)
    {
        using var img = new Image<Rgba32>(width, height, new Rgba32(107, 203, 119, 255));
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private async Task<HttpResponseMessage> PostUpdateProfileMultipartAsync(
        HttpClient client,
        string formCsrf,
        string? avatarUrl = null,
        string? uploadFileName = null,
        string? uploadMime = null,
        byte[]? uploadBytes = null,
        string fullName = "Avatar Test Athlete",
        string phone = "0907778899",
        string email = "avatar@spike.test")
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(formCsrf), "__RequestVerificationToken");
        content.Add(new StringContent(fullName), "fullName");
        content.Add(new StringContent(phone), "phone");
        content.Add(new StringContent(email), "email");
        if (avatarUrl != null)
        {
            content.Add(new StringContent(avatarUrl), "avatar");
        }
        if (uploadFileName != null && uploadBytes != null)
        {
            var fileContent = new ByteArrayContent(uploadBytes);
            if (!string.IsNullOrWhiteSpace(uploadMime))
            {
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(uploadMime);
            }
            content.Add(fileContent, "avatarFile", uploadFileName);
        }

        return await client.PostAsync("/Account/UpdateProfile", content);
    }

    [Fact]
    public async Task P01_ProfilePage_RendersAvatarStudio_WithoutManualUrlInput_AndShowsEightCartoonPresets()
    {
        var client = CreateCookieClient();
        await LoginMemberAsync(client);

        var res = await client.GetAsync("/Account/Profile");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        string html = WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());

        Assert.Contains("enctype=\"multipart/form-data\"", html);
        Assert.Contains("id=\"avatarStudio\"", html);
        Assert.Contains("id=\"profAvatarFile\"", html);
        Assert.Contains("accept=\".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp\"", html);
        Assert.Contains("id=\"selectedPresetAvatar\" name=\"avatar\"", html);

        // Manual URL input UI must be completely removed
        Assert.DoesNotContain("Hoặc dán đường dẫn ảnh (URL)", html);
        Assert.DoesNotContain("avatarClearUrlBtn", html);
        Assert.DoesNotContain("Xóa URL", html);
        Assert.DoesNotContain("id=\"profAvatar\"", html);

        foreach (string presetFile in new[] { "sunny.png", "ace.png", "mochi.png", "coco.png", "maya.png", "leo.png", "kiko.png", "kai.png" })
        {
            Assert.Contains($"/images/avatars/{presetFile}", html);
        }

        Assert.DoesNotContain("images.unsplash.com", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task P02_UpdateProfile_ValidImageUpload_SavesSanitizedWebp_UpdatesDb_AndDeletesPreviousUpload()
    {
        var client = CreateCookieClient();
        await LoginMemberAsync(client);

        // First upload: PNG
        string profileHtml1 = await client.GetStringAsync("/Account/Profile");
        string csrf1 = ExtractFormCsrfToken(profileHtml1);
        byte[] pngBytes = CreateTestPngBytes();

        var res1 = await PostUpdateProfileMultipartAsync(client, csrf1, "/images/avatars/avatar-01.svg", "my-photo.png", "image/png", pngBytes);
        Assert.Equal(HttpStatusCode.Redirect, res1.StatusCode);

        string firstSavedUrl;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var acc = await db.TbAccounts.AsNoTracking().FirstAsync(a => a.AccountId == _memberId);
            firstSavedUrl = acc.Avatar!;
        }

        Assert.StartsWith($"/uploads/avatars/spk-avatar-{_memberId}-", firstSavedUrl);
        Assert.EndsWith(".webp", firstSavedUrl);

        string firstDiskPath = Path.Combine(_tempWebRoot, firstSavedUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(firstDiskPath), "Uploaded WebP avatar file must exist on disk.");

        var savedInfo = await Image.IdentifyAsync(firstDiskPath);
        Assert.NotNull(savedInfo);
        Assert.Equal(360, savedInfo!.Width);
        Assert.Equal(360, savedInfo.Height);

        // Second upload: JPG -> must replace DB path AND delete old uploaded WebP file
        string profileHtml2 = await client.GetStringAsync("/Account/Profile");
        string csrf2 = ExtractFormCsrfToken(profileHtml2);
        byte[] jpgBytes = CreateTestJpegBytes();

        var res2 = await PostUpdateProfileMultipartAsync(client, csrf2, firstSavedUrl, "selfie.jpg", "image/jpeg", jpgBytes);
        Assert.Equal(HttpStatusCode.Redirect, res2.StatusCode);

        string secondSavedUrl;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var acc = await db.TbAccounts.AsNoTracking().FirstAsync(a => a.AccountId == _memberId);
            secondSavedUrl = acc.Avatar!;
        }

        Assert.NotEqual(firstSavedUrl, secondSavedUrl);
        Assert.StartsWith($"/uploads/avatars/spk-avatar-{_memberId}-", secondSavedUrl);
        Assert.False(File.Exists(firstDiskPath), "Old uploaded avatar file should be deleted when replaced.");
    }

    [Fact]
    public async Task P03_UpdateProfile_ValidatesUploadSizeFormatContent_AndUrlSafety_WhileSupportingCartoonPresets()
    {
        var client = CreateCookieClient();
        await LoginMemberAsync(client);

        // Save a valid preset URL first
        string html0 = await client.GetStringAsync("/Account/Profile");
        var presetRes = await PostUpdateProfileMultipartAsync(client, ExtractFormCsrfToken(html0), "/images/avatars/avatar-03.svg");
        Assert.Equal(HttpStatusCode.Redirect, presetRes.StatusCode);

        // Reject oversized upload (> 2 MB)
        string html1 = await client.GetStringAsync("/Account/Profile");
        byte[] tooLarge = new byte[(2 * 1024 * 1024) + 128];
        var oversizeRes = await PostUpdateProfileMultipartAsync(client, ExtractFormCsrfToken(html1), "/images/avatars/avatar-03.svg", "huge.png", "image/png", tooLarge);
        Assert.Equal(HttpStatusCode.Redirect, oversizeRes.StatusCode);
        string afterOversize = WebUtility.HtmlDecode(await client.GetStringAsync("/Account/Profile"));
        Assert.Contains("2 MB", afterOversize);

        // Reject disallowed extension (.svg)
        string html2 = afterOversize;
        var svgRes = await PostUpdateProfileMultipartAsync(client, ExtractFormCsrfToken(html2), "/images/avatars/avatar-03.svg", "xss.svg", "image/svg+xml", Encoding.UTF8.GetBytes("<svg/>"));
        Assert.Equal(HttpStatusCode.Redirect, svgRes.StatusCode);
        string afterSvg = WebUtility.HtmlDecode(await client.GetStringAsync("/Account/Profile"));
        Assert.Contains("JPG, JPEG, PNG", afterSvg);

        // Reject spoofed .png containing invalid image bytes
        var spoofRes = await PostUpdateProfileMultipartAsync(client, ExtractFormCsrfToken(afterSvg), "/images/avatars/avatar-03.svg", "fake.png", "image/png", Encoding.UTF8.GetBytes("NOT_A_PNG_FILE_PAYLOAD"));
        Assert.Equal(HttpStatusCode.Redirect, spoofRes.StatusCode);
        string afterSpoof = WebUtility.HtmlDecode(await client.GetStringAsync("/Account/Profile"));
        Assert.Contains("ảnh hợp lệ", afterSpoof);

        // Reject unsafe javascript: URL
        var xssUrlRes = await PostUpdateProfileMultipartAsync(client, ExtractFormCsrfToken(afterSpoof), "javascript:alert(1)");
        Assert.Equal(HttpStatusCode.Redirect, xssUrlRes.StatusCode);
        string afterXssUrl = WebUtility.HtmlDecode(await client.GetStringAsync("/Account/Profile"));
        Assert.Contains("không hợp lệ", afterXssUrl);

        // Verify stored avatar stayed cleanly on the saved preset!
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var acc = await db.TbAccounts.AsNoTracking().FirstAsync(a => a.AccountId == _memberId);
        Assert.Equal("/images/avatars/avatar-03.svg", acc.Avatar);
    }

    [Fact]
    public async Task P04_AjaxProfileSave_SynchronizesPublicNavbar_AdminShell_InitialsFallback_AndPersistsAcrossLogout()
    {
        var memberClient = CreateCookieClient();
        await LoginMemberAsync(memberClient);

        // 1. Account WITHOUT a saved avatar falls back to initial badge across Profile & Navbar (zero fake avatar)
        string homeWithoutAvatar = await memberClient.GetStringAsync("/");
        Assert.Contains("data-user-avatar=\"true\"", homeWithoutAvatar);
        string emptyNavImg = Regex.Match(homeWithoutAvatar, @"<img[^>]*data-user-avatar-img=""true""[^>]*>").Value;
        string emptyNavSpan = Regex.Match(homeWithoutAvatar, @"<span[^>]*data-user-avatar-initials=""true""[^>]*>").Value;
        Assert.NotEmpty(emptyNavImg);
        Assert.NotEmpty(emptyNavSpan);
        Assert.Matches(@"(?<!-)\bhidden\b", emptyNavImg);
        Assert.DoesNotMatch(@"(?<!-)\bhidden\b", emptyNavSpan);

        // 2. AJAX upload save via X-Requested-With updates DB and returns authoritative avatar + CSRF without redirect
        string profileHtml = await memberClient.GetStringAsync("/Account/Profile");
        string csrf = ExtractFormCsrfToken(profileHtml);

        using var ajaxContent = new MultipartFormDataContent();
        ajaxContent.Add(new StringContent(csrf), "__RequestVerificationToken");
        ajaxContent.Add(new StringContent("Avatar Test Athlete"), "fullName");
        ajaxContent.Add(new StringContent("0907778899"), "phone");
        ajaxContent.Add(new StringContent("avatar@spike.test"), "email");
        var fileBytes = new ByteArrayContent(CreateTestPngBytes());
        fileBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        ajaxContent.Add(fileBytes, "avatarFile", "navbar-sync.png");

        using var ajaxReq = new HttpRequestMessage(HttpMethod.Post, "/Account/UpdateProfile") { Content = ajaxContent };
        ajaxReq.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var ajaxRes = await memberClient.SendAsync(ajaxReq);
        Assert.Equal(HttpStatusCode.OK, ajaxRes.StatusCode);

        using var jsonDoc = JsonDocument.Parse(await ajaxRes.Content.ReadAsStringAsync());
        Assert.True(jsonDoc.RootElement.GetProperty("ok").GetBoolean());
        string savedUrl = jsonDoc.RootElement.GetProperty("avatar").GetString()!;
        Assert.StartsWith($"/uploads/avatars/spk-avatar-{_memberId}-", savedUrl);

        // 3. Global public pages (/, /Booking, /Product) immediately render saved avatar in navbar without initials showing
        foreach (string route in new[] { "/", "/Booking", "/Product" })
        {
            string pageHtml = await memberClient.GetStringAsync(route);
            string navImgTag = Regex.Match(pageHtml, @"<img[^>]*data-user-avatar-img=""true""[^>]*>").Value;
            string navSpanTag = Regex.Match(pageHtml, @"<span[^>]*data-user-avatar-initials=""true""[^>]*>").Value;
            Assert.Contains($"src=\"{savedUrl}\"", navImgTag);
            Assert.DoesNotMatch(@"(?<!-)\bhidden\b", navImgTag);
            Assert.Matches(@"(?<!-)\bhidden\b", navSpanTag);
        }

        // 4. Invalid AJAX save returns 400 JSON error without overwriting DB avatar
        using var badAjaxContent = new MultipartFormDataContent();
        badAjaxContent.Add(new StringContent(jsonDoc.RootElement.GetProperty("csrfToken").GetString() ?? csrf), "__RequestVerificationToken");
        badAjaxContent.Add(new StringContent("A"), "fullName"); // too short -> invalid
        badAjaxContent.Add(new StringContent("0907778899"), "phone");
        using var badAjaxReq = new HttpRequestMessage(HttpMethod.Post, "/Account/UpdateProfile") { Content = badAjaxContent };
        badAjaxReq.Headers.Add("X-Requested-With", "XMLHttpRequest");
        var badRes = await memberClient.SendAsync(badAjaxReq);
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);

        // 5. Logout & login again -> saved avatar persists across pages!
        await memberClient.GetAsync("/Account/Logout");
        await LoginMemberAsync(memberClient);
        string homeAfterRelogin = await memberClient.GetStringAsync("/");
        Assert.Contains($"src=\"{savedUrl}\"", homeAfterRelogin);

        // 6. Admin user saves preset avatar -> BOTH Admin sidebar (.sh-nav-user-avatar) AND Admin topbar sync cleanly!
        var adminClient = CreateCookieClient();
        await LoginUserAsync(adminClient, "avatar_admin", "AdminPass!2026");
        string adminProfileHtml = await adminClient.GetStringAsync("/Account/Profile");
        var adminPresetRes = await PostUpdateProfileMultipartAsync(
            adminClient,
            ExtractFormCsrfToken(adminProfileHtml),
            "/images/avatars/avatar-06.svg",
            fullName: "Quan Tri Vien",
            phone: "0907779900",
            email: "avataradmin@spike.test");
        Assert.Equal(HttpStatusCode.Redirect, adminPresetRes.StatusCode);

        string adminDashboard = await adminClient.GetStringAsync("/Admin");
        Assert.Contains("src=\"/images/avatars/avatar-06.svg\"", adminDashboard);
        Assert.Contains("sh-nav-user-avatar", adminDashboard);
        Assert.Contains("sh-topbar-user-pill", adminDashboard);
    }
}
