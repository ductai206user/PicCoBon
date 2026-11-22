using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;
using PickleballClub.Logging;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;
using Xunit;

namespace PickleballClub.Tests;

public sealed class CapturedLogEntry
{
    public LogLevel Level { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public Exception? Exception { get; init; }
}

public sealed class InMemoryLogCaptureProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    public ConcurrentQueue<CapturedLogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    public void Dispose() { }

    private sealed class CaptureLogger : ILogger
    {
        private readonly InMemoryLogCaptureProvider _owner;
        private readonly string _category;

        public CaptureLogger(InMemoryLogCaptureProvider owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            _owner._scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string? cid = null;
            _owner._scopeProvider.ForEachScope((s, _) =>
            {
                if (cid != null) return;
                if (s is IEnumerable<KeyValuePair<string, object?>> kvs)
                {
                    foreach (var kv in kvs)
                    {
                        if (string.Equals(kv.Key, "CorrelationId", StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                        {
                            cid = kv.Value.ToString();
                            return;
                        }
                    }
                }
            }, (object?)null);

            cid ??= CorrelationContext.CurrentId;

            _owner.Entries.Enqueue(new CapturedLogEntry
            {
                Level = logLevel,
                Category = _category,
                Message = formatter(state, exception),
                CorrelationId = cid,
                Exception = exception
            });
        }
    }
}

public sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T currentValue) => CurrentValue = currentValue;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public class LoggingUnitTests
{
    [Fact]
    public void L01_CleanSpikeConsoleFormatter_FormatsCompactSingleLineWithCategoryAndCorrelation()
    {
        var opts = new TestOptionsMonitor<CleanSpikeConsoleFormatterOptions>(new CleanSpikeConsoleFormatterOptions
        {
            ColorBehavior = LoggerColorBehavior.Disabled,
            IncludeCorrelationId = true,
            UseUtcTimestamp = false
        });

        using var formatter = new CleanSpikeConsoleFormatter(opts);
        var scopeProvider = new LoggerExternalScopeProvider();
        using var scope = scopeProvider.Push(new Dictionary<string, object?> { ["CorrelationId"] = "spk-abc123def456" });
        using var writer = new StringWriter();

        var entry = new LogEntry<string>(
            LogLevel.Warning,
            "PickleballClub.Services.PayPalPaymentService",
            new EventId(0),
            "PayPal attempt PP-100 awaiting reconciliation",
            null,
            (s, _) => s);

        formatter.Write(in entry, scopeProvider, writer);
        string line = writer.ToString().TrimEnd();

        Assert.Matches(@"^\d{2}:\d{2}:\d{2}\s+WARN\s+PayPal\s+PayPal attempt PP-100 awaiting reconciliation \[cid:spk-abc123def456\]$", line);
    }

    [Fact]
    public void L01b_CleanSpikeConsoleFormatter_FormatsExceptionDetailsAndCategoryShortNames()
    {
        Assert.Equal("SpikeOS", CleanSpikeConsoleFormatter.FormatCategoryName("PickleballClub.Program"));
        Assert.Equal("Database", CleanSpikeConsoleFormatter.FormatCategoryName("Microsoft.EntityFrameworkCore.Database.Command"));
        Assert.Equal("Booking", CleanSpikeConsoleFormatter.FormatCategoryName("PickleballClub.Services.BookingService"));
        Assert.Equal("ProShop", CleanSpikeConsoleFormatter.FormatCategoryName("PickleballClub.Services.OrderService"));
        Assert.Equal("PayPal", CleanSpikeConsoleFormatter.FormatCategoryName("PickleballClub.Services.PayPalClient"));
        Assert.Equal("QrCheckIn", CleanSpikeConsoleFormatter.FormatCategoryName("PickleballClub.Services.QrTokenService"));
        Assert.Equal("Auth", CleanSpikeConsoleFormatter.FormatCategoryName("PickleballClub.Controllers.AccountController"));

        var opts = new TestOptionsMonitor<CleanSpikeConsoleFormatterOptions>(new CleanSpikeConsoleFormatterOptions
        {
            ColorBehavior = LoggerColorBehavior.Disabled
        });
        using var formatter = new CleanSpikeConsoleFormatter(opts);
        using var writer = new StringWriter();
        var ex = new InvalidOperationException("Simulated SQL failure");

        var entry = new LogEntry<string>(
            LogLevel.Error,
            "Microsoft.EntityFrameworkCore.Database.Command",
            new EventId(20102),
            "Failed executing DbCommand",
            ex,
            (s, _) => s);

        formatter.Write(in entry, null, writer);
        string text = writer.ToString();
        Assert.Contains("ERROR  Database", text);
        Assert.Contains("InvalidOperationException: Simulated SQL failure", text);
    }

    [Fact]
    public void L02_CorrelationContext_SanitizesInvalidHeadersAndMasksPii()
    {
        Assert.Equal("spk-client-01", CorrelationContext.ResolveSafeId("  spk-client-01  "));

        string forged = CorrelationContext.ResolveSafeId("evil\r\n21:00:00 INFO Admin Bypass");
        Assert.StartsWith("spk-", forged);
        Assert.DoesNotContain("\r", forged);
        Assert.DoesNotContain("\n", forged);

        Assert.Equal("us***@example.com", CorrelationContext.MaskIdentifier("user@example.com"));
        Assert.Equal("09***89", CorrelationContext.MaskIdentifier("0901234589"));
        Assert.Equal("***", CorrelationContext.MaskIdentifier("abc"));
    }

    [Fact]
    public void L03_EnvironmentConfigFiles_ConfigureSafeDevelopmentSqlDebugAndProduction()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        var devCfg = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "appsettings.json"))
            .AddJsonFile(Path.Combine(root, "appsettings.Development.json"))
            .Build();

        Assert.Equal("Warning", devCfg["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"]);
        Assert.Equal("Warning", devCfg["Logging:LogLevel:System.Net.Http.HttpClient"]);
        Assert.Equal("spike-clean", devCfg["Logging:Console:FormatterName"]);

        var sqlDebugCfg = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "appsettings.json"))
            .AddJsonFile(Path.Combine(root, "appsettings.SqlDebug.json"))
            .Build();

        Assert.Equal("Information", sqlDebugCfg["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"]);
        Assert.Equal("spike-clean", sqlDebugCfg["Logging:Console:FormatterName"]);

        var prodCfg = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "appsettings.json"))
            .AddJsonFile(Path.Combine(root, "appsettings.Production.json"))
            .Build();

        Assert.Equal("Warning", prodCfg["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"]);
        Assert.Equal("Warning", prodCfg["Logging:LogLevel:Microsoft.AspNetCore"]);
        Assert.Equal("json", prodCfg["Logging:Console:FormatterName"]);
    }
}

public class LoggingPipelineIntegrationTests : IAsyncLifetime
{
    private const string MasterConnectionString =
        "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private readonly string _dbName = "PickleballClub_LogTest_" + Guid.NewGuid().ToString("N")[..8];
    private string ConnectionString =>
        $"Server=.\\SQLEXPRESS;Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private readonly FakePayPalClient _fakePayPal = new();
    private readonly InMemoryLogCaptureProvider _logSink = new();
    private WebApplicationFactory<Program>? _factory;
    private int _courtId;
    private int _productId;

    public async Task InitializeAsync()
    {
        string appRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddJsonFile(Path.Combine(appRoot, "appsettings.Development.json"), optional: false);
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                    ["Security:QrSigningKey"] = "LoggingTest-HMAC-SecretKey-2026-SpikePickleball-32Chars!",
                    ["BackgroundWorkers:Enabled"] = "false",
                    ["RateLimiting:AuthPermitLimit"] = "1000",
                    ["RateLimiting:CheckInPermitLimit"] = "1000",
                    ["RateLimiting:BookingPermitLimit"] = "1000",
                    ["RateLimiting:PayPalPermitLimit"] = "1000",
                    ["PayPal:Environment"] = "Sandbox",
                    ["PayPal:BaseUrl"] = PayPalOptions.OfficialSandboxBaseUrl,
                    ["PayPal:ClientId"] = "sbx-log-client-id",
                    ["PayPal:ClientSecret"] = "SECRET_SHOULD_NEVER_BE_LOGGED_12345",
                    ["PayPal:WebhookId"] = "WH-LOG-01",
                    ["PayPal:Currency"] = "USD",
                    ["PayPal:VndPerUsd"] = "26000"
                });
            });

            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(_logSink);
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<PickleballContext>>();
                services.AddDbContext<PickleballContext>(opts => opts.UseSqlServer(ConnectionString));
                services.RemoveAll<IPayPalClient>();
                services.AddSingleton<IPayPalClient>(_fakePayPal);
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var pwd = scope.ServiceProvider.GetRequiredService<PasswordService>();
        await db.Database.MigrateAsync();

        var member = new TbAccount
        {
            Username = "log_member",
            FullName = "Log Test Member",
            Phone = "0905556677",
            Email = "logmember@spike.test",
            RoleId = 2,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        member.Password = pwd.HashPassword(member, "MemberPass!2026");

        var staff = new TbAccount
        {
            Username = "log_staff",
            FullName = "Log Test Staff",
            Phone = "0905556688",
            Email = "logstaff@spike.test",
            RoleId = 3,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        staff.Password = pwd.HashPassword(staff, "StaffPass!2026");

        db.TbAccounts.AddRange(member, staff);

        var court = new TbCourt
        {
            CourtName = "Sân Log Test 01",
            CourtType = "Outdoor",
            TypeLabel = "Ngoài trời",
            Surface = "Acrylic",
            BasePrice = 120_000,
            Surcharge = 0,
            IsActive = true
        };
        db.TbCourts.Add(court);

        var cat = new TbProductCategory { Title = "Đồ Uống", Alias = "do-uong", IsActive = true };
        db.TbProductCategories.Add(cat);
        await db.SaveChangesAsync();

        var prod = new TbProduct
        {
            Title = "Nước Ion",
            CategoryProductId = cat.CategoryProductId,
            Price = 26_000,
            UnitInStock = 2,
            Quantity = 2,
            IsActive = true
        };
        db.TbProducts.Add(prod);
        await db.SaveChangesAsync();

        _courtId = court.CourtId;
        _productId = prod.ProductId;
    }

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }
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

    [Fact]
    public async Task L04_EndToEndPipeline_SuppressesEfSelectNoise_PropagatesCorrelation_PreservesSqlErrors_AndHidesSecrets()
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        async Task<string> FetchCsrf()
        {
            var r = await client.GetAsync("/api/security/csrf");
            r.EnsureSuccessStatusCode();
            using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            return d.RootElement.GetProperty("token").GetString()!;
        }

        async Task<HttpResponseMessage> PostJson(string url, object payload, string? correlationId = null, string? bookingToken = null)
        {
            string csrf = await FetchCsrf();
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("RequestVerificationToken", csrf);
            if (!string.IsNullOrEmpty(correlationId)) req.Headers.Add(CorrelationContext.HeaderName, correlationId);
            if (!string.IsNullOrEmpty(bookingToken)) req.Headers.Add("X-Booking-Access-Token", bookingToken);
            return await client.SendAsync(req);
        }

        // 1. Trigger routine SELECT queries & test forged Correlation header cleanup
        using (var stateReq = new HttpRequestMessage(HttpMethod.Get, "/api/db/state"))
        {
            stateReq.Headers.Add(CorrelationContext.HeaderName, "invalid$char!12");
            var stateRes = await client.SendAsync(stateReq);
            Assert.Equal(HttpStatusCode.OK, stateRes.StatusCode);
            Assert.True(stateRes.Headers.TryGetValues(CorrelationContext.HeaderName, out var cidVals));
            Assert.StartsWith("spk-", cidVals!.First());
        }

        // Routine EF Core Command Info logs must be suppressed!
        Assert.DoesNotContain(_logSink.Entries, e =>
            e.Category == "Microsoft.EntityFrameworkCore.Database.Command" && e.Level == LogLevel.Information);

        // 2. Authentication failure must log warning without leaking password
        var badLogin = await PostJson("/api/auth/login", new { identifier = "log_member", password = "WrongSecretPass!999" }, "trace-auth-01");
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);

        // Login legitimately
        var okLogin = await PostJson("/api/auth/login", new { identifier = "log_member", password = "MemberPass!2026" }, "trace-auth-02");
        Assert.Equal(HttpStatusCode.OK, okLogin.StatusCode);

        // 3. End-to-end correlated PayPal Checkout -> CreateOrder -> Capture -> Ledger flow
        const string traceCid = "trace-paypal-e2e-01";
        string dateIso = DateOnly.FromDateTime(BookingService.GetVietnamNow().AddDays(3)).ToString("yyyy-MM-dd");
        var quoteRes = await client.GetAsync($"/api/bookings/quote?courtId={_courtId}&date={dateIso}&time=10:00&duration=60");
        quoteRes.EnsureSuccessStatusCode();
        using var quoteDoc = JsonDocument.Parse(await quoteRes.Content.ReadAsStringAsync());
        int amountDue = quoteDoc.RootElement.GetProperty("quote").GetProperty("amountDue").GetInt32();
        string pricingVersion = quoteDoc.RootElement.GetProperty("pricingVersion").GetString()!;

        var bkRes = await PostJson("/api/bookings", new
        {
            courtId = _courtId,
            customerName = "Log Test Member",
            customerPhone = "0905556677",
            date = dateIso,
            time = "10:00",
            duration = 60,
            players = 4,
            paymentMethod = "paypal",
            pricingVersion,
            expectedAmountDue = amountDue,
            idempotencyKey = Guid.NewGuid().ToString("N")
        }, traceCid);
        Assert.Equal(HttpStatusCode.OK, bkRes.StatusCode);
        using var bkDoc = JsonDocument.Parse(await bkRes.Content.ReadAsStringAsync());
        string bookingCode = bkDoc.RootElement.GetProperty("booking").GetProperty("id").GetString()!;

        string capturedState = string.Empty;
        string ppOrderId = "PPORD-LOGTEST-01";
        _fakePayPal.OnCreateOrder = (req, _) =>
        {
            var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(req.ReturnUrl).Query);
            capturedState = q["state"].ToString();
            _fakePayPal.Orders[ppOrderId] = (req.AmountUsd, req.CurrencyCode, "CREATED", null);
            return Task.FromResult(new PayPalCreateOrderResponse(
                true, ppOrderId, "CREATED",
                $"https://www.sandbox.paypal.com/checkoutnow?token={ppOrderId}&secret_token=HIDDEN_QUERY_TOKEN",
                "{\"status\":\"CREATED\"}", 201));
        };

        var ppStartRes = await PostJson("/api/payments/paypal/create-order", new
        {
            targetType = "booking",
            targetCode = bookingCode,
            idempotencyKey = Guid.NewGuid().ToString("N")
        }, traceCid);
        Assert.Equal(HttpStatusCode.OK, ppStartRes.StatusCode);
        _fakePayPal.OnCreateOrder = null;

        _fakePayPal.ApproveOrder(ppOrderId);
        var ppCapRes = await PostJson("/api/payments/paypal/capture", new
        {
            state = capturedState,
            payPalOrderId = ppOrderId
        }, traceCid);
        Assert.Equal(HttpStatusCode.OK, ppCapRes.StatusCode);

        // 4. Stock conflict & QR tamper logs
        var overOrder = await PostJson("/api/orders/create", new
        {
            customerName = "Log Test Member",
            phone = "0905556677",
            paymentMethod = "cash",
            idempotencyKey = Guid.NewGuid().ToString("N"),
            items = new[] { new { productId = _productId, qty = 50 } }
        }, "trace-order-conflict-01");
        Assert.Equal(HttpStatusCode.Conflict, overOrder.StatusCode);

        // Login as staff for check-in QR validation test
        await PostJson("/api/auth/login", new { identifier = "log_staff", password = "StaffPass!2026" }, "trace-staff-login");
        var badQr = await PostJson("/api/checkin/verify", new { tokenOrCode = "SPK1.FAKEPAYLOAD.FAKESIG_PRIVATE" }, "trace-qr-fail-01");
        Assert.Equal(HttpStatusCode.BadRequest, badQr.StatusCode);

        // 5. Real SQL failure must STILL log an Error from Database.Command
        using (var errScope = _factory.Services.CreateScope())
        {
            var db = errScope.ServiceProvider.GetRequiredService<PickleballContext>();
            await Assert.ThrowsAsync<SqlException>(() =>
                db.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM dbo.NonExistentLogAuditTable_999"));
        }

        // Assert log assertions across all captured entries!
        var snapshot = _logSink.Entries.ToArray();

        Assert.Contains(snapshot, e => e.CorrelationId == traceCid && e.Message.Contains("Booking") && e.Message.Contains(bookingCode));
        Assert.Contains(snapshot, e => e.CorrelationId == traceCid && e.Category.Contains("PayPal") && e.Message.Contains("created"));
        Assert.Contains(snapshot, e => e.CorrelationId == traceCid && e.Category.Contains("PaymentController") && e.Message.Contains("redirect initiated"));
        Assert.Contains(snapshot, e => e.CorrelationId == traceCid && e.Category.Contains("PayPal") && e.Message.Contains("capture completed"));
        Assert.Contains(snapshot, e => e.CorrelationId == "trace-auth-01" && e.Level == LogLevel.Warning && e.Message.Contains("InvalidPassword"));
        Assert.Contains(snapshot, e => e.CorrelationId == "trace-order-conflict-01" && e.Level == LogLevel.Warning && e.Message.Contains("insufficient stock"));
        Assert.Contains(snapshot, e => e.CorrelationId == "trace-qr-fail-01" && e.Level == LogLevel.Warning && e.Message.Contains("SignatureMismatch"));
        Assert.Contains(snapshot, e => e.Category == "Microsoft.EntityFrameworkCore.Database.Command" && e.Level == LogLevel.Error);

        // Security audit: secrets, passwords, full QR payloads, and ApproveUrl tokens NEVER appear in any log entry!
        foreach (var log in snapshot)
        {
            string combined = $"{log.Message} {log.Exception?.Message}";
            Assert.DoesNotContain("SECRET_SHOULD_NEVER_BE_LOGGED_12345", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("WrongSecretPass!999", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("MemberPass!2026", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("FAKESIG_PRIVATE", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("HIDDEN_QUERY_TOKEN", combined, StringComparison.Ordinal);
        }
    }
}
