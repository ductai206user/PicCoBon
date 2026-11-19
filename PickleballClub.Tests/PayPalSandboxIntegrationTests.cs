using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PickleballClub.Configuration;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;
using Xunit;

namespace PickleballClub.Tests;

public sealed class FakePayPalClient : IPayPalClient
{
    private int _seq = 0;
    public int CreateCallCount => Volatile.Read(ref _createCalls);
    public int GetOrderCallCount => Volatile.Read(ref _getCalls);
    public int CaptureCallCount => Volatile.Read(ref _captureCalls);
    public int RefundCallCount => Volatile.Read(ref _refundCalls);
    public int VerifyWebhookCallCount => Volatile.Read(ref _verifyWebhookCalls);

    private int _createCalls;
    private int _getCalls;
    private int _captureCalls;
    private int _refundCalls;
    private int _verifyWebhookCalls;

    public Func<PayPalCreateOrderRequest, CancellationToken, Task<PayPalCreateOrderResponse>>? OnCreateOrder { get; set; }
    public Func<string, CancellationToken, Task<PayPalOrderSnapshot>>? OnGetOrder { get; set; }
    public Func<string, string, CancellationToken, Task<PayPalOrderSnapshot>>? OnCaptureOrder { get; set; }
    public Func<string, decimal?, string, string, string, CancellationToken, Task<PayPalRefundResult>>? OnRefundCapture { get; set; }
    public Func<PayPalWebhookHeaders, JsonElement, CancellationToken, Task<bool>>? OnVerifyWebhook { get; set; }

    public ConcurrentDictionary<string, (decimal AmountUsd, string Currency, string OrderStatus, string? CaptureId)> Orders { get; } = new();

    public void Reset()
    {
        Interlocked.Exchange(ref _createCalls, 0);
        Interlocked.Exchange(ref _getCalls, 0);
        Interlocked.Exchange(ref _captureCalls, 0);
        Interlocked.Exchange(ref _refundCalls, 0);
        Interlocked.Exchange(ref _verifyWebhookCalls, 0);
        OnCreateOrder = null;
        OnGetOrder = null;
        OnCaptureOrder = null;
        OnRefundCapture = null;
        OnVerifyWebhook = null;
    }

    public async Task<PayPalCreateOrderResponse> CreateOrderAsync(PayPalCreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _createCalls);
        if (OnCreateOrder != null)
        {
            return await OnCreateOrder(request, cancellationToken);
        }

        string oid = $"PPORD-{Interlocked.Increment(ref _seq):D6}";
        Orders[oid] = (request.AmountUsd, request.CurrencyCode, "CREATED", null);
        return new PayPalCreateOrderResponse(
            Success: true,
            PayPalOrderId: oid,
            PayPalStatus: "CREATED",
            ApproveUrl: $"https://www.sandbox.paypal.com/checkoutnow?token={oid}",
            SanitizedSummaryJson: "{\"id\":\"" + oid + "\",\"status\":\"CREATED\"}",
            HttpStatusCode: 201);
    }

    public async Task<PayPalOrderSnapshot> GetOrderAsync(string payPalOrderId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _getCalls);
        if (OnGetOrder != null)
        {
            return await OnGetOrder(payPalOrderId, cancellationToken);
        }

        if (!Orders.TryGetValue(payPalOrderId, out var rec))
        {
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null, false, 404, "RESOURCE_NOT_FOUND", "PayPal order not found");
        }

        return new PayPalOrderSnapshot(
            Success: true,
            PayPalOrderId: payPalOrderId,
            OrderStatus: rec.OrderStatus,
            CaptureId: rec.CaptureId,
            CaptureStatus: rec.OrderStatus == "COMPLETED" ? "COMPLETED" : null,
            AmountUsd: rec.AmountUsd,
            CurrencyCode: rec.Currency,
            PayerId: "PAYER-SBX-001",
            PayerEmail: "sbx-buyer@example.com",
            SanitizedSummaryJson: "{\"id\":\"" + payPalOrderId + "\",\"status\":\"" + rec.OrderStatus + "\"}"
        );
    }

    public async Task<PayPalOrderSnapshot> CaptureOrderAsync(string payPalOrderId, string requestId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _captureCalls);
        if (OnCaptureOrder != null)
        {
            return await OnCaptureOrder(payPalOrderId, requestId, cancellationToken);
        }

        return ExecuteDefaultCapture(payPalOrderId);
    }

    public PayPalOrderSnapshot ExecuteDefaultCapture(string payPalOrderId)
    {
        if (!Orders.TryGetValue(payPalOrderId, out var rec))
        {
            return new PayPalOrderSnapshot(false, payPalOrderId, null, null, null, null, null, null, null, null, false, 404, "RESOURCE_NOT_FOUND", "PayPal order not found");
        }

        string capId = rec.CaptureId ?? $"PPCAP-{Interlocked.Increment(ref _seq):D6}";
        Orders[payPalOrderId] = (rec.AmountUsd, rec.Currency, "COMPLETED", capId);

        return new PayPalOrderSnapshot(
            Success: true,
            PayPalOrderId: payPalOrderId,
            OrderStatus: "COMPLETED",
            CaptureId: capId,
            CaptureStatus: "COMPLETED",
            AmountUsd: rec.AmountUsd,
            CurrencyCode: rec.Currency,
            PayerId: "PAYER-SBX-001",
            PayerEmail: "sbx-buyer@example.com",
            SanitizedSummaryJson: "{\"id\":\"" + payPalOrderId + "\",\"captureId\":\"" + capId + "\",\"status\":\"COMPLETED\"}");
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amountUsd, string currencyCode, string noteToPayer, string requestId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _refundCalls);
        if (OnRefundCapture != null)
        {
            return await OnRefundCapture(captureId, amountUsd, currencyCode, noteToPayer, requestId, cancellationToken);
        }

        string rfdId = $"PPRFD-{Interlocked.Increment(ref _seq):D6}";
        return new PayPalRefundResult(
            Success: true,
            RefundId: rfdId,
            RefundStatus: "COMPLETED",
            AmountUsd: amountUsd,
            CurrencyCode: currencyCode,
            SanitizedSummaryJson: "{\"id\":\"" + rfdId + "\",\"status\":\"COMPLETED\"}");
    }

    public async Task<bool> VerifyWebhookSignatureAsync(PayPalWebhookHeaders headers, JsonElement rawWebhookEvent, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _verifyWebhookCalls);
        if (OnVerifyWebhook != null)
        {
            return await OnVerifyWebhook(headers, rawWebhookEvent, cancellationToken);
        }

        return string.Equals(headers.TransmissionSig, "VALID_SBX_SIG", StringComparison.Ordinal);
    }

    public void ApproveOrder(string payPalOrderId)
    {
        if (Orders.TryGetValue(payPalOrderId, out var rec))
        {
            Orders[payPalOrderId] = (rec.AmountUsd, rec.Currency, "APPROVED", rec.CaptureId);
        }
    }
}

public sealed class PayPalTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string MasterConnectionString =
        "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public string DatabaseName { get; } = "PickleballClub_PayPalTest_" + Guid.NewGuid().ToString("N")[..8];
    public string ConnectionString =>
        $"Server=.\\SQLEXPRESS;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public FakePayPalClient FakePayPal { get; } = new();
    public int AdminAccountId { get; private set; }
    public int StaffAccountId { get; private set; }
    public int CustomerAAccountId { get; private set; }
    public int CustomerBAccountId { get; private set; }
    public int Court1Id { get; private set; }
    public int Court2Id { get; private set; }
    public int Product1Id { get; private set; }

    public PayPalTestWebAppFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Security:QrSigningKey"] = "PayPalTest-HMAC-SecretKey-2026-SpikePickleball-32CharsPlus!",
                ["BackgroundWorkers:Enabled"] = "false",
                ["RateLimiting:AuthPermitLimit"] = "1000",
                ["RateLimiting:CheckInPermitLimit"] = "1000",
                ["RateLimiting:BookingPermitLimit"] = "1000",
                ["RateLimiting:PayPalPermitLimit"] = "1000",
                ["PayPal:Environment"] = "Sandbox",
                ["PayPal:BaseUrl"] = PayPalOptions.OfficialSandboxBaseUrl,
                ["PayPal:ClientId"] = "sbx-test-client-id",
                ["PayPal:ClientSecret"] = "sbx-test-client-secret",
                ["PayPal:WebhookId"] = "WH-SBX-TEST-01",
                ["PayPal:Currency"] = "USD",
                ["PayPal:VndPerUsd"] = "26000"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PickleballContext>>();
            services.AddDbContext<PickleballContext>(opts => opts.UseSqlServer(ConnectionString));
            services.RemoveAll<IPayPalClient>();
            services.AddSingleton<IPayPalClient>(FakePayPal);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var pwd = scope.ServiceProvider.GetRequiredService<PasswordService>();

        await db.Database.MigrateAsync();

        var admin = new TbAccount
        {
            Username = "pp_admin",
            FullName = "Admin PayPal Test",
            Phone = "0903000001",
            Email = "ppadmin@spike.test",
            RoleId = 1,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        admin.Password = pwd.HashPassword(admin, "AdminPass!2026");

        var staff = new TbAccount
        {
            Username = "pp_staff",
            FullName = "Staff PayPal Test",
            Phone = "0903000003",
            Email = "ppstaff@spike.test",
            RoleId = 3,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        staff.Password = pwd.HashPassword(staff, "StaffPass!2026");

        var userA = new TbAccount
        {
            Username = "pp_member_a",
            FullName = "Hội Viên A",
            Phone = "0903000011",
            Email = "ppa@spike.test",
            RoleId = 2,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        userA.Password = pwd.HashPassword(userA, "MemberAPass!2026");

        var userB = new TbAccount
        {
            Username = "pp_member_b",
            FullName = "Hội Viên B",
            Phone = "0903000012",
            Email = "ppb@spike.test",
            RoleId = 2,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        userB.Password = pwd.HashPassword(userB, "MemberBPass!2026");

        db.TbAccounts.AddRange(admin, staff, userA, userB);

        var c1 = new TbCourt
        {
            CourtName = "Sân PayPal Test 01",
            CourtType = "Outdoor",
            TypeLabel = "Ngoài trời",
            Surface = "Hard Court",
            BasePrice = 120_000,
            Surcharge = 0,
            IsActive = true
        };
        var c2 = new TbCourt
        {
            CourtName = "Sân PayPal VIP 02",
            CourtType = "Indoor",
            TypeLabel = "Trong nhà VIP",
            Surface = "Cushion",
            BasePrice = 120_000,
            Surcharge = 20_000,
            IsActive = true
        };
        db.TbCourts.AddRange(c1, c2);

        var cat = new TbProductCategory { Title = "Nước uống", Alias = "nuoc-uong", IsActive = true };
        db.TbProductCategories.Add(cat);
        await db.SaveChangesAsync();

        var prod1 = new TbProduct
        {
            Title = "Pocari Sweat 500ml",
            CategoryProductId = cat.CategoryProductId,
            Price = 26_000,
            UnitInStock = 100,
            Quantity = 100,
            IsActive = true
        };
        db.TbProducts.Add(prod1);
        await db.SaveChangesAsync();

        AdminAccountId = admin.AccountId;
        StaffAccountId = staff.AccountId;
        CustomerAAccountId = userA.AccountId;
        CustomerBAccountId = userB.AccountId;
        Court1Id = c1.CourtId;
        Court2Id = c2.CourtId;
        Product1Id = prod1.ProductId;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(MasterConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
IF DB_ID(N'{DatabaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{DatabaseName}];
END";
        await cmd.ExecuteNonQueryAsync();
    }
}

public class PayPalSandboxIntegrationTests : IClassFixture<PayPalTestWebAppFactory>
{
    private readonly PayPalTestWebAppFactory _factory;
    private static int _slotSeed = 1;

    public PayPalSandboxIntegrationTests(PayPalTestWebAppFactory factory)
    {
        _factory = factory;
        _factory.FakePayPal.Reset();
    }

    private HttpClient CreateCookieClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static (string DateIso, string TimeStr) NextUniqueFutureSlot()
    {
        int idx = Interlocked.Increment(ref _slotSeed);
        int dayOffset = 2 + (idx / 12);
        int hour = 6 + (idx % 12);
        var dt = DateOnly.FromDateTime(BookingService.GetVietnamNow()).AddDays(dayOffset);
        return (dt.ToString("yyyy-MM-dd"), $"{hour:D2}:00");
    }

    private static async Task<string> FetchCsrfAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/security/csrf");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostJsonWithCsrfAsync(HttpClient client, string url, object payload, string? headerBookingToken = null)
    {
        string csrf = await FetchCsrfAsync(client);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("RequestVerificationToken", csrf);
        if (!string.IsNullOrWhiteSpace(headerBookingToken))
        {
            req.Headers.Add("X-Booking-Access-Token", headerBookingToken);
        }
        return await client.SendAsync(req);
    }

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = CreateCookieClient();
        var resp = await PostJsonWithCsrfAsync(client, "/api/auth/login", new { identifier = username, password });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return client;
    }

    private async Task<(string BookingCode, int AmountDue, string? GuestToken)> CreatePayPalBookingAsync(HttpClient client, int? courtId = null, string? name = null, string? phone = null)
    {
        // Release active pending holds from prior independent test cases so maxActivePendingPerActor=3 stays strictly tested in domain logic without cross-test coupling
        using (var cleanScope = _factory.Services.CreateScope())
        {
            var dbClean = cleanScope.ServiceProvider.GetRequiredService<PickleballContext>();
            await dbClean.TbBookings
                .Where(b => b.Status == "pending")
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, "expired"));
        }

        var (dateIso, timeStr) = NextUniqueFutureSlot();
        int targetCourt = courtId ?? _factory.Court1Id;

        var quoteResp = await client.GetAsync($"/api/bookings/quote?courtId={targetCourt}&date={dateIso}&time={Uri.EscapeDataString(timeStr)}&duration=60");
        Assert.Equal(HttpStatusCode.OK, quoteResp.StatusCode);
        using var quoteDoc = JsonDocument.Parse(await quoteResp.Content.ReadAsStringAsync());
        var q = quoteDoc.RootElement.GetProperty("quote");
        int amountDue = q.GetProperty("amountDue").GetInt32();
        string pricingVersion = quoteDoc.RootElement.GetProperty("pricingVersion").GetString()!;

        var bkResp = await PostJsonWithCsrfAsync(client, "/api/bookings", new
        {
            courtId = targetCourt,
            customerName = name ?? "Khách PayPal",
            customerPhone = phone ?? "0909111222",
            date = dateIso,
            time = timeStr,
            duration = 60,
            players = 4,
            paymentMethod = "paypal",
            pricingVersion,
            expectedAmountDue = amountDue,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        string body = await bkResp.Content.ReadAsStringAsync();
        Assert.True(bkResp.StatusCode == HttpStatusCode.OK, $"Booking failed {bkResp.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        string code = doc.RootElement.GetProperty("booking").GetProperty("id").GetString()!;
        string? guestTok = doc.RootElement.TryGetProperty("guestAccessToken", out var gt) ? gt.GetString() : null;
        return (code, amountDue, guestTok);
    }

    private static (string State, string PayPalOrderId) ExtractReturnParams(string approveUrl, HttpClient client, string returnUrlOrStateUrl)
    {
        var uri = new Uri(returnUrlOrStateUrl);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        return (query["state"].ToString(), uri.Host);
    }

    private async Task<(string AttemptCode, string PayPalOrderId, string StateToken, decimal AmountUsd)> StartPayPalCheckoutAndCaptureReturnStateAsync(
        HttpClient client,
        string targetType,
        string targetCode,
        string? guestAccessToken = null,
        string? idemKey = null)
    {
        string capturedReturnUrl = string.Empty;
        string assignedOrderId = $"PPORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        _factory.FakePayPal.OnCreateOrder = (req, _) =>
        {
            capturedReturnUrl = req.ReturnUrl;
            _factory.FakePayPal.Orders[assignedOrderId] = (req.AmountUsd, req.CurrencyCode, "CREATED", null);
            return Task.FromResult(new PayPalCreateOrderResponse(
                true,
                assignedOrderId,
                "CREATED",
                $"https://www.sandbox.paypal.com/checkoutnow?token={assignedOrderId}",
                "{\"status\":\"CREATED\"}",
                201));
        };

        var resp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/create-order", new
        {
            targetType,
            targetCode,
            guestAccessToken,
            idempotencyKey = idemKey ?? Guid.NewGuid().ToString("N")
        }, guestAccessToken);

        _factory.FakePayPal.OnCreateOrder = null;
        string body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"Create PayPal order failed {resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(capturedReturnUrl).Query);
        return (
            root.GetProperty("attemptCode").GetString()!,
            root.GetProperty("payPalOrderId").GetString()!,
            q["state"].ToString(),
            root.GetProperty("amountUsd").GetDecimal()
        );
    }

    [Fact]
    public async Task TC01_Booking_CreatePayPalCheckout_StoresPendingBookingAndPayPalAttempt_At26000Rate()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, amountDue, _) = await CreatePayPalBookingAsync(client);

        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);

        decimal expectedUsd = Math.Round(amountDue / 26000m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(expectedUsd, started.AmountUsd);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
        Assert.Equal("paypal", bk.PaymentMethod);
        Assert.Equal("pending", bk.Status);
        Assert.Equal("unpaid", bk.PaymentStatus);
        Assert.Equal(0, bk.PaidAmount);

        var pp = await db.TbPayPalTransactions.AsNoTracking().FirstAsync(p => p.PublicAttemptCode == started.AttemptCode);
        Assert.Equal("AwaitingApproval", pp.LocalStatus);
        Assert.Equal(26000m, pp.ExchangeRateVndPerUsd);
        Assert.Equal(amountDue, pp.AmountVnd);
        Assert.Equal(expectedUsd, pp.AmountUsd);
    }

    [Fact]
    public async Task TC02_Booking_ApprovedReturnAndCapture_PostsLedgerWithoutMutatingBookingStatus()
    {
        using var guestClient = CreateCookieClient();
        var (bookingCode, amountDue, guestTok) = await CreatePayPalBookingAsync(guestClient);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(guestClient, "booking", bookingCode, guestTok);

        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        // GET /Payment/PayPalReturn must execute ZERO DB mutations (Rule 9)
        var returnGet = await guestClient.GetAsync($"/Payment/PayPalReturn?state={Uri.EscapeDataString(started.StateToken)}&token={started.PayPalOrderId}&PayerID=PAYER1");
        Assert.Equal(HttpStatusCode.OK, returnGet.StatusCode);

        using (var midScope = _factory.Services.CreateScope())
        {
            var dbMid = midScope.ServiceProvider.GetRequiredService<PickleballContext>();
            var midAttempt = await dbMid.TbPayPalTransactions.AsNoTracking().FirstAsync(x => x.PublicAttemptCode == started.AttemptCode);
            Assert.Equal("AwaitingApproval", midAttempt.LocalStatus);
            Assert.Empty(await dbMid.TbPaymentTransactions.Where(t => t.BookingCode == bookingCode).ToListAsync());
        }

        // POST /api/payments/paypal/capture captures APPROVED order & writes ledger
        var capResp = await PostJsonWithCsrfAsync(guestClient, "/api/payments/paypal/capture", new
        {
            state = started.StateToken,
            payPalOrderId = started.PayPalOrderId
        });
        string capJson = await capResp.Content.ReadAsStringAsync();
        Assert.True(capResp.StatusCode == HttpStatusCode.OK, $"Capture failed: {capJson}");

        using var capDoc = JsonDocument.Parse(capJson);
        Assert.Equal("Completed", capDoc.RootElement.GetProperty("localStatus").GetString());
        string captureId = capDoc.RootElement.GetProperty("payPalCaptureId").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(captureId));
        Assert.False(string.IsNullOrWhiteSpace(capDoc.RootElement.GetProperty("booking").GetProperty("qrSvg").GetString()));

        using (var postScope = _factory.Services.CreateScope())
        {
            var db = postScope.ServiceProvider.GetRequiredService<PickleballContext>();
            var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
            Assert.Equal("pending", bk.Status); // Operational status untouched!
            Assert.Equal("paid", bk.PaymentStatus);
            Assert.Equal("paypal", bk.PaymentMethod);
            Assert.Equal(amountDue, bk.PaidAmount);

            var ledgerRows = await db.TbPaymentTransactions.AsNoTracking().Where(t => t.BookingId == bk.BookingId).ToListAsync();
            Assert.Single(ledgerRows);
            Assert.Equal("paypal", ledgerRows[0].PaymentMethod);
            Assert.Equal(captureId, ledgerRows[0].ReferenceCode);
            Assert.Equal(amountDue, ledgerRows[0].Amount);
        }

        // Admin reports summary includes paypalNetCollected
        using var adminClient = await LoginAsync("pp_admin", "AdminPass!2026");
        var repResp = await adminClient.GetAsync("/api/admin/reports/summary");
        Assert.Equal(HttpStatusCode.OK, repResp.StatusCode);
        using var repDoc = JsonDocument.Parse(await repResp.Content.ReadAsStringAsync());
        Assert.True(repDoc.RootElement.GetProperty("paypalNetCollected").GetInt32() >= amountDue);
    }

    [Fact]
    public async Task TC03_Booking_CancelFlow_MarksCancelled_AndCreatesZeroLedgerRows()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, _, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);

        var getCancel = await client.GetAsync($"/Payment/PayPalCancel?state={Uri.EscapeDataString(started.StateToken)}&token={started.PayPalOrderId}");
        Assert.Equal(HttpStatusCode.OK, getCancel.StatusCode);

        var postCancel = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/cancel", new
        {
            state = started.StateToken,
            payPalOrderId = started.PayPalOrderId
        });
        Assert.Equal(HttpStatusCode.OK, postCancel.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var pp = await db.TbPayPalTransactions.AsNoTracking().FirstAsync(x => x.PublicAttemptCode == started.AttemptCode);
        Assert.Equal("Cancelled", pp.LocalStatus);
        var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
        Assert.Equal("unpaid", bk.PaymentStatus);
        Assert.Equal(0, bk.PaidAmount);
        Assert.Empty(await db.TbPaymentTransactions.Where(t => t.BookingCode == bookingCode).ToListAsync());
    }

    [Fact]
    public async Task TC04_InvalidPayPalCredentials_CreateOrderFailsClosed502_AndMarksAttemptCreateFailed()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, _, _) = await CreatePayPalBookingAsync(client);

        _factory.FakePayPal.OnCreateOrder = (_, _) => Task.FromResult(
            new PayPalCreateOrderResponse(false, null, null, null, null, 502, "PAYPAL_AUTH_ERROR", "Invalid PayPal client credentials"));

        var resp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/create-order", new
        {
            targetType = "booking",
            targetCode = bookingCode,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.BadGateway, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var attempt = await db.TbPayPalTransactions.AsNoTracking().Where(x => x.TargetCode == bookingCode).OrderByDescending(x => x.PayPalTransactionId).FirstAsync();
        Assert.Equal("CreateFailed", attempt.LocalStatus);
        Assert.Equal("PAYPAL_AUTH_ERROR", attempt.LastErrorCode);
    }

    [Fact]
    public async Task TC05_MissingExchangeRate_VndPerUsdZero_BlocksPayPalCheckoutWith503()
    {
        using var noRateFactory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PayPal:VndPerUsd"] = "0"
                });
            });
        });

        using var client = noRateFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var cfgResp = await client.GetAsync("/api/payments/paypal/config");
        Assert.Equal(HttpStatusCode.OK, cfgResp.StatusCode);
        using var cfgDoc = JsonDocument.Parse(await cfgResp.Content.ReadAsStringAsync());
        Assert.False(cfgDoc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal("PAYPAL_EXCHANGE_RATE_MISSING", cfgDoc.RootElement.GetProperty("disabledCode").GetString());

        var bkResp = await PostJsonWithCsrfAsync(client, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "No Rate User",
            customerPhone = "0909333444",
            date = NextUniqueFutureSlot().DateIso,
            time = "10:00",
            duration = 60,
            players = 4,
            paymentMethod = "paypal",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, bkResp.StatusCode);
    }

    [Fact]
    public async Task TC06_ForgedReturnStateOrMismatchedOrderId_Rejected400_AndZeroCaptures()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, _, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);

        // Forged state signature
        var forgedResp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new
        {
            state = "forged-tampered-state-token",
            payPalOrderId = started.PayPalOrderId
        });
        Assert.Equal(HttpStatusCode.BadRequest, forgedResp.StatusCode);

        // Valid state with swapped PayPal Order ID
        var swappedOrderResp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new
        {
            state = started.StateToken,
            payPalOrderId = "PPORD-OTHER-999"
        });
        Assert.Equal(HttpStatusCode.BadRequest, swappedOrderResp.StatusCode);
        Assert.Equal(0, _factory.FakePayPal.CaptureCallCount);
    }

    [Fact]
    public async Task TC07_DuplicateCaptureCallbackAndF5Replay_CallsPayPalCaptureOnce()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, amountDue, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        var cap1 = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        var cap2 = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });

        Assert.Equal(HttpStatusCode.OK, cap1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cap2.StatusCode);
        using var doc2 = JsonDocument.Parse(await cap2.Content.ReadAsStringAsync());
        Assert.True(doc2.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(1, _factory.FakePayPal.CaptureCallCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        Assert.Equal(1, await db.TbPaymentTransactions.CountAsync(t => t.BookingCode == bookingCode && t.TransactionType == "payment"));
        Assert.Equal(amountDue, (await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode)).PaidAmount);
    }

    [Fact]
    public async Task TC08_ConcurrentParallelCaptureRequests_PreventDuplicateCaptureAndLedgerEntries()
    {
        using var c1 = await LoginAsync("pp_member_a", "MemberAPass!2026");
        using var c2 = await LoginAsync("pp_member_a", "MemberAPass!2026");

        var (bookingCode, amountDue, _) = await CreatePayPalBookingAsync(c1);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(c1, "booking", bookingCode);
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        var enteredCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _factory.FakePayPal.OnCaptureOrder = async (oid, reqId, ct) =>
        {
            enteredCapture.TrySetResult();
            await releaseCapture.Task.WaitAsync(ct);
            _factory.FakePayPal.OnCaptureOrder = null;
            return _factory.FakePayPal.ExecuteDefaultCapture(oid);
        };

        var task1 = PostJsonWithCsrfAsync(c1, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        await enteredCapture.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Second request arrives while first is mid-HTTP capture
        var task2 = PostJsonWithCsrfAsync(c2, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        var r2 = await task2;
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);

        releaseCapture.SetResult();
        var r1 = await task1;
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(1, _factory.FakePayPal.CaptureCallCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
        Assert.Equal(amountDue, bk.PaidAmount);
        Assert.Equal(1, await db.TbPaymentTransactions.CountAsync(t => t.BookingId == bk.BookingId));
    }

    [Fact]
    public async Task TC09_WrongCapturedAmountOrCurrency_Rejected_AndFlaggedWithoutMarkingPaid()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, _, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);

        _factory.FakePayPal.Orders[started.PayPalOrderId] = (started.AmountUsd, "USD", "APPROVED", null);
        _factory.FakePayPal.OnCaptureOrder = (oid, _, _) => Task.FromResult(
            new PayPalOrderSnapshot(true, oid, "COMPLETED", "CAP-BAD-AMT", "COMPLETED", 1.00m, "EUR", "P1", "p@x.com", "{}"));

        var resp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var pp = await db.TbPayPalTransactions.AsNoTracking().FirstAsync(x => x.PublicAttemptCode == started.AttemptCode);
        Assert.Equal("CompensationRefundRequired", pp.LocalStatus);
        Assert.Equal("CAP-BAD-AMT", pp.PayPalCaptureId);
        var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
        Assert.Equal("unpaid", bk.PaymentStatus);
        Assert.Equal(0, bk.PaidAmount);
    }

    [Fact]
    public async Task TC10_PayerActionRequiredStatus_NeverCallsCaptureEndpoint_Returns409()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, _, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);

        _factory.FakePayPal.Orders[started.PayPalOrderId] = (started.AmountUsd, "USD", "PAYER_ACTION_REQUIRED", null);

        var resp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("PAYPAL_APPROVAL_REQUIRED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, _factory.FakePayPal.CaptureCallCount);
    }

    [Fact]
    public async Task TC11_HoldExpiredBeforeCapture_AbortsWithoutCallingPayPalCapture()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, _, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        // Expire booking hold in DB BEFORE buyer invokes /capture
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var bk = await db.TbBookings.FirstAsync(b => b.BookingCode == bookingCode);
            bk.HoldExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var resp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal(0, _factory.FakePayPal.CaptureCallCount);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var pp = await db.TbPayPalTransactions.AsNoTracking().FirstAsync(x => x.PublicAttemptCode == started.AttemptCode);
            Assert.Equal("ExpiredBeforeCapture", pp.LocalStatus);
        }
    }

    [Fact]
    public async Task TC12_HoldExpiredDuringPayPalCapture_PreservesCaptureEvidence_ExecutesCompensationRefund()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, amountDue, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        // Simulate hold expiring RIGHT WHILE CaptureOrderAsync HTTP is in flight!
        _factory.FakePayPal.OnCaptureOrder = async (oid, reqId, ct) =>
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
                var bk = await db.TbBookings.FirstAsync(b => b.BookingCode == bookingCode, ct);
                bk.HoldExpiresAtUtc = DateTime.UtcNow.AddSeconds(-10);
                await db.SaveChangesAsync(ct);
            }
            _factory.FakePayPal.OnCaptureOrder = null;
            return _factory.FakePayPal.ExecuteDefaultCapture(oid);
        };

        var resp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("PAYPAL_CAPTURED_AND_REFUNDED", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, _factory.FakePayPal.CaptureCallCount);
        Assert.Equal(1, _factory.FakePayPal.RefundCallCount);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PickleballContext>();
        var pp = await verifyDb.TbPayPalTransactions.AsNoTracking().FirstAsync(x => x.PublicAttemptCode == started.AttemptCode);
        Assert.Equal("RefundedAfterExpiry", pp.LocalStatus);
        Assert.False(string.IsNullOrWhiteSpace(pp.PayPalCaptureId));
        Assert.False(string.IsNullOrWhiteSpace(pp.PayPalRefundId));

        var ledger = await verifyDb.TbPaymentTransactions.AsNoTracking().Where(t => t.BookingCode == bookingCode).OrderBy(t => t.TransactionId).ToListAsync();
        Assert.Equal(2, ledger.Count); // receipt (+amountDue) AND offsetting refund (-amountDue)!
        Assert.Equal("payment", ledger[0].TransactionType);
        Assert.Equal("refund", ledger[1].TransactionType);
        Assert.Equal(0, await BookingService.ComputeBookingLedgerNetPaidAsync(verifyDb, ledger[0].BookingId!.Value));

        var bkAfter = await verifyDb.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
        Assert.Equal("refunded", bkAfter.PaymentStatus);
        Assert.Equal(0, bkAfter.PaidAmount);
    }

    [Fact]
    public async Task TC13_HoldExpiredWithTransientRefundFailure_PreservesEvidenceAndReconcilesLater()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, amountDue, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        _factory.FakePayPal.OnCaptureOrder = async (oid, reqId, ct) =>
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
                var bk = await db.TbBookings.FirstAsync(b => b.BookingCode == bookingCode, ct);
                bk.HoldExpiresAtUtc = DateTime.UtcNow.AddSeconds(-10);
                await db.SaveChangesAsync(ct);
            }
            _factory.FakePayPal.OnCaptureOrder = null;
            return _factory.FakePayPal.ExecuteDefaultCapture(oid);
        };

        // Fail immediate compensation refund
        _factory.FakePayPal.OnRefundCapture = (_, _, _, _, _, _) => Task.FromResult(
            new PayPalRefundResult(false, null, null, null, null, null, true, 503, "PAYPAL_TEMP_DOWN", "Simulated outage"));

        var resp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);

        using (var midScope = _factory.Services.CreateScope())
        {
            var db = midScope.ServiceProvider.GetRequiredService<PickleballContext>();
            var pp = await db.TbPayPalTransactions.AsNoTracking().FirstAsync(x => x.PublicAttemptCode == started.AttemptCode);
            Assert.Equal("CompensationRefundRequired", pp.LocalStatus);
            Assert.False(string.IsNullOrWhiteSpace(pp.PayPalCaptureId)); // Evidence preserved!
            Assert.Equal(amountDue, (await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode)).PaidAmount);
        }

        // Recover PayPal Refund endpoint and run background reconciler
        _factory.FakePayPal.OnRefundCapture = null;
        using (var workerScope = _factory.Services.CreateScope())
        {
            var db = workerScope.ServiceProvider.GetRequiredService<PickleballContext>();
            var ppSvc = workerScope.ServiceProvider.GetRequiredService<PayPalPaymentService>();
            int processed = await ppSvc.ReconcileStaleAttemptsAsync(db);
            Assert.True(processed >= 1);

            var ppAfter = await db.TbPayPalTransactions.AsNoTracking().FirstAsync(x => x.PublicAttemptCode == started.AttemptCode);
            Assert.Equal("RefundedAfterExpiry", ppAfter.LocalStatus);
            Assert.False(string.IsNullOrWhiteSpace(ppAfter.PayPalRefundId));
            Assert.Equal(0, (await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode)).PaidAmount);
        }
    }

    [Fact]
    public async Task TC14_ProShopStandaloneOrder_CreateAndCapturePayPal_PostsOrderLedger()
    {
        using var guestClient = CreateCookieClient();
        var ordResp = await PostJsonWithCsrfAsync(guestClient, "/api/orders/create", new
        {
            customerName = "Khách Pro Shop PayPal",
            phone = "0908887777",
            address = "Nhận tại quầy",
            paymentMethod = "paypal",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            items = new[] { new { productId = _factory.Product1Id, qty = 2 } }
        });
        string ordJson = await ordResp.Content.ReadAsStringAsync();
        Assert.True(ordResp.StatusCode == HttpStatusCode.OK, $"Order failed: {ordJson}");
        using var ordDoc = JsonDocument.Parse(ordJson);
        string orderCode = ordDoc.RootElement.GetProperty("orderCode").GetString()!;
        int totalVnd = ordDoc.RootElement.GetProperty("totalAmount").GetInt32();
        Assert.Equal(52_000, totalVnd); // 2 * 26,000 = 52,000 VND = $2.00 USD

        // Guest cookie was set automatically; create PayPal checkout without even passing explicit token!
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(guestClient, "order", orderCode);
        Assert.Equal(2.00m, started.AmountUsd);

        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);
        var capResp = await PostJsonWithCsrfAsync(guestClient, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        Assert.Equal(HttpStatusCode.OK, capResp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var ord = await db.TbOrders.AsNoTracking().FirstAsync(o => o.Code == orderCode);
        Assert.Equal("paypal", ord.PaymentMethod);
        Assert.Equal("paid", ord.PaymentStatus);
        Assert.Equal(totalVnd, ord.PaidAmount);
    }

    [Fact]
    public async Task TC15_ProShopAttachedOrderToActiveBooking_SettlesLinkedBookingWithoutDoubleCharge()
    {
        // Seed an approved active TODAY booking for Member A (using safe non-expired time-of-day window)
        string todayIso = DateOnly.FromDateTime(BookingService.GetVietnamNow()).ToString("yyyy-MM-dd");
        string activeCode = $"PB-MERG{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<PickleballContext>();
            db.TbBookings.Add(new TbBooking
            {
                BookingCode = activeCode,
                CourtId = _factory.Court1Id,
                AccountId = _factory.CustomerAAccountId,
                CustomerName = "Hội Viên A",
                Phone = "0903000011",
                BookingDate = todayIso,
                StartTime = "22:30",
                Duration = 120,
                Players = 4,
                CourtFee = 120_000,
                PosTotal = 0,
                AmountDue = 120_000,
                PaidAmount = 0,
                TotalPaid = 0,
                PaymentMethod = "paypal",
                PaymentStatus = "unpaid",
                Status = "approved",
                OrderStatusId = 2,
                PosItemsJson = "[]",
                CreatedDate = DateTime.Now
            });
            await db.SaveChangesAsync();
        }

        using var memberClient = await LoginAsync("pp_member_a", "MemberAPass!2026");

        // Attach 1 drink (26,000 VND) onto active booking -> booking total becomes 146,000 VND
        var ordResp = await PostJsonWithCsrfAsync(memberClient, "/api/orders/create", new
        {
            customerName = "Hội Viên A",
            phone = "0903000011",
            bookingCode = activeCode,
            paymentMethod = "paypal",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            items = new[] { new { productId = _factory.Product1Id, qty = 1 } }
        });
        Assert.Equal(HttpStatusCode.OK, ordResp.StatusCode);
        using var ordDoc = JsonDocument.Parse(await ordResp.Content.ReadAsStringAsync());
        string mergedOrderCode = ordDoc.RootElement.GetProperty("orderCode").GetString()!;

        // Direct PayPal checkout on a merged order MUST be blocked 409 ORDER_MERGED_TO_BOOKING
        var directOrdPayPal = await PostJsonWithCsrfAsync(memberClient, "/api/payments/paypal/create-order", new
        {
            targetType = "order",
            targetCode = mergedOrderCode,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.Conflict, directOrdPayPal.StatusCode);

        // Pay via linked booking once -> collects exact single combined balance 146,000 VND
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(memberClient, "booking", activeCode);
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        var capResp = await PostJsonWithCsrfAsync(memberClient, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        Assert.Equal(HttpStatusCode.OK, capResp.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<PickleballContext>();
        var bk = await verifyDb.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == activeCode);
        Assert.Equal(146_000, bk.AmountDue);
        Assert.Equal(146_000, bk.PaidAmount);
        Assert.Equal("paid", bk.PaymentStatus);
        Assert.Equal(1, await verifyDb.TbPaymentTransactions.CountAsync(t => t.BookingId == bk.BookingId));
    }

    [Fact]
    public async Task TC16_UnauthorizedCallerAndWrongGuestToken_Blocked403_OnCreateAndCapture()
    {
        using var ownerClient = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, _, _) = await CreatePayPalBookingAsync(ownerClient);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(ownerClient, "booking", bookingCode);

        // Wrong member B tries to create PayPal checkout & capture owner's session
        using var attackerClient = await LoginAsync("pp_member_b", "MemberBPass!2026");
        var stealCreate = await PostJsonWithCsrfAsync(attackerClient, "/api/payments/paypal/create-order", new
        {
            targetType = "booking",
            targetCode = bookingCode,
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.Forbidden, stealCreate.StatusCode);

        var stealCapture = await PostJsonWithCsrfAsync(attackerClient, "/api/payments/paypal/capture", new
        {
            state = started.StateToken,
            payPalOrderId = started.PayPalOrderId
        });
        Assert.Equal(HttpStatusCode.Forbidden, stealCapture.StatusCode);

        // Anonymous caller with wrong guest token gets 403
        using var anonClient = CreateCookieClient();
        var badTokenResp = await PostJsonWithCsrfAsync(anonClient, "/api/payments/paypal/create-order", new
        {
            targetType = "booking",
            targetCode = bookingCode,
            guestAccessToken = "bk_forged_wrong_token",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.Forbidden, badTokenResp.StatusCode);
    }

    [Fact]
    public async Task TC17_CaptureTimeout_ReconcilesViaGetOrderWithoutSecondCapture()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, amountDue, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        // First /capture simulates gateway timeout after PayPal actually completed!
        _factory.FakePayPal.OnCaptureOrder = (oid, _, _) =>
        {
            _factory.FakePayPal.Orders[oid] = (started.AmountUsd, "USD", "COMPLETED", "CAP-TIMEOUT-OK");
            return Task.FromResult(new PayPalOrderSnapshot(false, oid, null, null, null, null, null, null, null, null, IsAmbiguousTimeout: true, 504, "TIMEOUT", "Gateway timeout"));
        };
        // Next probe inside same call also times out temporarily so AmbiguousTimeout is persisted
        int getCount = 0;
        _factory.FakePayPal.OnGetOrder = (oid, ct) =>
        {
            if (++getCount == 1)
            {
                return Task.FromResult(new PayPalOrderSnapshot(true, oid, "APPROVED", null, null, started.AmountUsd, "USD", "P1", "p@x.com", "{}"));
            }
            if (getCount == 2)
            {
                _factory.FakePayPal.OnGetOrder = null;
                return Task.FromResult(new PayPalOrderSnapshot(false, oid, null, null, null, null, null, null, null, null, IsAmbiguousTimeout: true, 504, "TIMEOUT", "Probe timed out"));
            }
            return _factory.FakePayPal.GetOrderAsync(oid, ct);
        };

        var timedOutResp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        Assert.Equal(HttpStatusCode.GatewayTimeout, timedOutResp.StatusCode);

        // Clear transient outage; user clicks Reconcile -> resolves via GetOrder WITHOUT calling CaptureOrderAsync again!
        _factory.FakePayPal.OnCaptureOrder = null;
        _factory.FakePayPal.OnGetOrder = null;
        int capturesBeforeReconcile = _factory.FakePayPal.CaptureCallCount;

        var recResp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/reconcile", new { attemptCode = started.AttemptCode });
        Assert.Equal(HttpStatusCode.OK, recResp.StatusCode);
        Assert.Equal(capturesBeforeReconcile, _factory.FakePayPal.CaptureCallCount); // ZERO extra capture calls!

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
        Assert.Equal(amountDue, bk.PaidAmount);
        Assert.Equal("paid", bk.PaymentStatus);
    }

    [Fact]
    public async Task TC18_BackgroundWorkerSweep_ReconcilesAgedAwaitingApprovalOrder()
    {
        using var client = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, amountDue, _) = await CreatePayPalBookingAsync(client);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(client, "booking", bookingCode);

        // Buyer approved on PayPal, then closed tab before hitting PayPalReturn
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        using (var ageScope = _factory.Services.CreateScope())
        {
            var db = ageScope.ServiceProvider.GetRequiredService<PickleballContext>();
            var row = await db.TbPayPalTransactions.FirstAsync(x => x.PublicAttemptCode == started.AttemptCode);
            row.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10);
            await db.SaveChangesAsync();

            var svc = ageScope.ServiceProvider.GetRequiredService<PayPalPaymentService>();
            int reconciled = await svc.ReconcileStaleAttemptsAsync(db);
            Assert.True(reconciled >= 1);

            var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
            Assert.Equal("paid", bk.PaymentStatus);
            Assert.Equal(amountDue, bk.PaidAmount);
        }
    }

    [Fact]
    public async Task TC19_AdminPayPalRefund_UpdatesSandboxAndLedger_AndBlocksManualPayPalSpoofing()
    {
        using var memberClient = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, amountDue, _) = await CreatePayPalBookingAsync(memberClient);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(memberClient, "booking", bookingCode);
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);
        var capResp = await PostJsonWithCsrfAsync(memberClient, "/api/payments/paypal/capture", new { state = started.StateToken, payPalOrderId = started.PayPalOrderId });
        Assert.Equal(HttpStatusCode.OK, capResp.StatusCode);

        using var adminClient = await LoginAsync("pp_admin", "AdminPass!2026");

        // Manual cashier endpoint MUST NOT allow spoofing paymentMethod="paypal"
        var spoofResp = await PostJsonWithCsrfAsync(adminClient, $"/api/admin/bookings/{bookingCode}/payments", new
        {
            amount = 10_000,
            paymentMethod = "paypal",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.BadRequest, spoofResp.StatusCode);

        // Admin executes genuine PayPal refund via PayPal Admin endpoint
        var rfdResp = await PostJsonWithCsrfAsync(adminClient, $"/api/admin/paypal/{started.AttemptCode}/refund", new
        {
            reason = "Khách đổi lịch",
            idempotencyKey = Guid.NewGuid().ToString("N")
        });
        Assert.Equal(HttpStatusCode.OK, rfdResp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var pp = await db.TbPayPalTransactions.AsNoTracking().FirstAsync(x => x.PublicAttemptCode == started.AttemptCode);
        Assert.Equal("Refunded", pp.LocalStatus);
        Assert.False(string.IsNullOrWhiteSpace(pp.PayPalRefundId));

        var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
        Assert.Equal(0, bk.PaidAmount);
        Assert.Equal("refunded", bk.PaymentStatus);
    }

    [Fact]
    public async Task TC20_PayPalWebhook_RejectsInvalidSignature401_AndCompletesVerifiedEvent()
    {
        using var memberClient = await LoginAsync("pp_member_a", "MemberAPass!2026");
        var (bookingCode, amountDue, _) = await CreatePayPalBookingAsync(memberClient);
        var started = await StartPayPalCheckoutAndCaptureReturnStateAsync(memberClient, "booking", bookingCode);
        _factory.FakePayPal.ApproveOrder(started.PayPalOrderId);

        using var anonClient = CreateCookieClient();
        string webhookJson = JsonSerializer.Serialize(new
        {
            id = "WH-EVT-001",
            event_type = "CHECKOUT.ORDER.APPROVED",
            resource = new { id = started.PayPalOrderId }
        });

        using (var badReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paypal")
        {
            Content = new StringContent(webhookJson, Encoding.UTF8, "application/json")
        })
        {
            badReq.Headers.Add("PAYPAL-TRANSMISSION-SIG", "FORGED_SIG");
            var badResp = await anonClient.SendAsync(badReq);
            Assert.Equal(HttpStatusCode.Unauthorized, badResp.StatusCode);
        }

        using (var okReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paypal")
        {
            Content = new StringContent(webhookJson, Encoding.UTF8, "application/json")
        })
        {
            okReq.Headers.Add("PAYPAL-TRANSMISSION-ID", "TX-001");
            okReq.Headers.Add("PAYPAL-TRANSMISSION-TIME", DateTime.UtcNow.ToString("O"));
            okReq.Headers.Add("PAYPAL-CERT-URL", "https://api.sandbox.paypal.com/cert.pem");
            okReq.Headers.Add("PAYPAL-AUTH-ALGO", "SHA256withRSA");
            okReq.Headers.Add("PAYPAL-TRANSMISSION-SIG", "VALID_SBX_SIG");
            var okResp = await anonClient.SendAsync(okReq);
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
        Assert.Equal("paid", bk.PaymentStatus);
        Assert.Equal(amountDue, bk.PaidAmount);
    }

    [Fact]
    public async Task TC21_MissingPayPalTableSchema_DisablesConfigAndReturns503InsteadOf500()
    {
        string preV5DbName = "PickleballClub_PreV5Test_" + Guid.NewGuid().ToString("N")[..8];
        string preV5Conn = $"Server=.\\SQLEXPRESS;Database={preV5DbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        string masterConn = "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

        try
        {
            using var preV5Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = preV5Conn,
                        ["Security:QrSigningKey"] = "PreV5Test-HMAC-SecretKey-2026-SpikePickleball-32CharsPlus!",
                        ["BackgroundWorkers:Enabled"] = "false",
                        ["PayPal:Environment"] = "Sandbox",
                        ["PayPal:BaseUrl"] = PayPalOptions.OfficialSandboxBaseUrl,
                        ["PayPal:ClientId"] = "sbx-test-client-id",
                        ["PayPal:ClientSecret"] = "sbx-test-client-secret",
                        ["PayPal:Currency"] = "USD",
                        ["PayPal:VndPerUsd"] = "26000"
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<PickleballContext>>();
                    services.AddDbContext<PickleballContext>(opts => opts.UseSqlServer(preV5Conn));
                });
            });

            using (var scope = preV5Factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
                var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
                await migrator.MigrateAsync("20261109123000_V4_HoldTtlIdempotencyAndOrderFulfillment");

                var cat = new TbProductCategory { Title = "Đồ Uống", Alias = "do-uong", IsActive = true };
                db.TbProductCategories.Add(cat);
                await db.SaveChangesAsync();
                db.TbProducts.Add(new TbProduct { Title = "Nước suối", CategoryProductId = cat.CategoryProductId, Price = 15000, UnitInStock = 10, Quantity = 10, IsActive = true });
                await db.SaveChangesAsync();
            }

            using var client = preV5Factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

            var cfgResp = await client.GetAsync("/api/payments/paypal/config");
            Assert.Equal(HttpStatusCode.OK, cfgResp.StatusCode);
            using var cfgDoc = JsonDocument.Parse(await cfgResp.Content.ReadAsStringAsync());
            Assert.False(cfgDoc.RootElement.GetProperty("enabled").GetBoolean());
            Assert.Equal("PAYPAL_SCHEMA_NOT_MIGRATED", cfgDoc.RootElement.GetProperty("disabledCode").GetString());

            var ordResp = await PostJsonWithCsrfAsync(client, "/api/orders/create", new
            {
                customerName = "PreV5 Guard",
                phone = "0909111333",
                paymentMethod = "paypal",
                idempotencyKey = Guid.NewGuid().ToString("N"),
                items = new[] { new { productId = 1, qty = 1 } }
            });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ordResp.StatusCode);
            using var ordDoc = JsonDocument.Parse(await ordResp.Content.ReadAsStringAsync());
            Assert.Equal("PAYPAL_SCHEMA_NOT_MIGRATED", ordDoc.RootElement.GetProperty("code").GetString());

            var ppCreateResp = await PostJsonWithCsrfAsync(client, "/api/payments/paypal/create-order", new
            {
                targetType = "order",
                targetCode = "ORD-ANY",
                idempotencyKey = Guid.NewGuid().ToString("N")
            });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ppCreateResp.StatusCode);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var conn = new SqlConnection(masterConn);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
IF DB_ID(N'{preV5DbName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{preV5DbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{preV5DbName}];
END";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
