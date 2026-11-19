using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace PickleballClub.Services;

public sealed class GuestRecoveryEntry
{
    public string BookingCode { get; set; } = string.Empty;
    public string GuestAccessToken { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public static class GuestRecoveryCookieHelper
{
    public const string CookieName = "spike_guest_bk";
    public const string ProtectorPurpose = "Spike.GuestBookingRecovery.v1";

    public static IDataProtector CreateProtector(IDataProtectionProvider provider) =>
        provider.CreateProtector(ProtectorPurpose);

    public static List<GuestRecoveryEntry> ReadEntries(HttpRequest request, IDataProtector protector)
    {
        try
        {
            if (!request.Cookies.TryGetValue(CookieName, out var rawCookie) ||
                string.IsNullOrWhiteSpace(rawCookie))
            {
                return new List<GuestRecoveryEntry>();
            }

            string json = protector.Unprotect(rawCookie);
            return JsonSerializer.Deserialize<List<GuestRecoveryEntry>>(json) ?? new List<GuestRecoveryEntry>();
        }
        catch
        {
            return new List<GuestRecoveryEntry>();
        }
    }

    public static Dictionary<string, string> ReadActiveTokenMap(HttpRequest request, IDataProtector protector)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DateTime utcNow = DateTime.UtcNow;
        foreach (var entry in ReadEntries(request, protector))
        {
            if (entry.ExpiresAtUtc > utcNow &&
                !string.IsNullOrWhiteSpace(entry.BookingCode) &&
                !string.IsNullOrWhiteSpace(entry.GuestAccessToken))
            {
                map[entry.BookingCode.Trim().ToUpperInvariant()] = entry.GuestAccessToken;
            }
        }
        return map;
    }

    public static void SaveProof(
        HttpRequest request,
        HttpResponse response,
        IDataProtector protector,
        string codeKey,
        string rawToken,
        string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(codeKey) || string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        try
        {
            string normalizedKey = codeKey.Trim().ToUpperInvariant();
            var entries = ReadEntries(request, protector);
            entries.RemoveAll(e =>
                e.ExpiresAtUtc <= DateTime.UtcNow ||
                string.Equals(e.BookingCode, normalizedKey, StringComparison.OrdinalIgnoreCase));

            entries.Insert(0, new GuestRecoveryEntry
            {
                BookingCode = normalizedKey,
                GuestAccessToken = rawToken,
                IdempotencyKey = IdempotencyService.NormalizeKey(idempotencyKey),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(4)
            });

            if (entries.Count > 8)
            {
                entries.RemoveRange(8, entries.Count - 8);
            }

            string json = JsonSerializer.Serialize(entries);
            string protectedPayload = protector.Protect(json);

            response.Cookies.Append(CookieName, protectedPayload, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddHours(4),
                IsEssential = true
            });
        }
        catch
        {
            // Non-fatal cookie failure
        }
    }
}
