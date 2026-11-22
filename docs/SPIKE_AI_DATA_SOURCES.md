# SPIKE OS — GEMINI AI ASSISTANT DATA SOURCES & TOOLS

## 1. Grounded Data Principles

Spike Assistant never answers club, pricing, court availability, inventory, or booking questions from unverified free-text assumptions. Every domain question invokes one or more of the **6 allowlisted read-only server-side tools** in `Services/SpikeAssistantToolService.cs`.

- **Zero Raw SQL / Zero Mutations**: No tool can create, update, or delete bookings, court locks, accounts, payments, or Pro Shop orders.
- **Zero Hallucinated Inventory or Prices**: If a court or product does not exist in `PickleballContext`, the tool returns the exact database state so the assistant reports verified inventory and pricing.
- **Advisory Disclaimer**: All availability and pricing responses explicitly state that results are real-time advisory snapshots and direct users to `/Booking` or `/Product` for authoritative checkout.

---

## 2. Allowlisted Read-Only Tools

### Tool 1: `get_club_info`
- **Source Services & Tables**:
  - `ClubSettingsService.GetSettingsAsync(PickleballContext)` (`TbSystemConfigs`)
  - `PickleballContext.TbCourts` (`AsNoTracking()`, `Where(c => c.IsActive)`)
- **Returned Fields**:
  - Club name, address, phone/hotline, email, opening & closing hours (`OpenHour`–`CloseHour`), peak hours (`PeakStart`–`PeakEnd`).
  - Pending hold timeout (`PendingHoldMinutes`), cancellation cutoff (`CancelCutoffMinutes`), early check-in window (`CheckInEarlyMinutes`), and active court list (`courtId`, `name`, `type`, `typeLabel`, `surface`).

### Tool 2: `check_court_availability`
- **Source Services & Tables**:
  - `ClubSettingsService.GetSettingsAsync(PickleballContext)`
  - `PickleballContext.TbCourts` (`IsActive == true`)
  - `PickleballContext.TbBookings` (`Status != "cancelled" && Status != "expired"`, filtered by `BookingService.IsPendingHoldActive(b, utcNow)` for `"pending"` holds)
  - `PickleballContext.TbCourtLocks` (active maintenance locks for the date)
  - `BookingService.GetVietnamNow()` (`SE Asia Standard Time`, UTC+7)
- **Parameters**:
  - `date`: `"today"`, `"tomorrow"`, `"tonight"`, or `"YYYY-MM-DD"` (clamped to `-1..+30` days from Vietnam today).
  - `startTime` (optional): `"HH:mm"` (e.g., `"18:00"`, `"19:00"`).
  - `durationMinutes` (optional): `60`–`240` (snapped to 30-minute increments).
  - `courtType` (optional): filters by court type or name (`Indoor`, `Outdoor`, `VIP`).
- **Behavior**:
  - When `startTime` is provided, checks exact interval overlap `[startMin, endMin)` against all active bookings, non-expired pending holds, and maintenance locks, returning which courts are open and suggesting up to 3 alternative start times on busy courts.
  - When `startTime` is omitted, scans hourly windows across operating hours (`OpenHour..CloseHour`) and summarizes available time slots per court.

### Tool 3: `get_pricing_quote`
- **Source Services & Tables**:
  - `ClubSettingsService.GetSettingsAsync(PickleballContext)`
  - `PricingService.CalculateQuote(startTime, durationMinutes, courtSurcharge, discountPercent, settings)`
  - `Configuration/PayPalOptions.cs` (`VndPerUsd`)
- **Parameters**:
  - `startTime` (optional, defaults to `"18:00"`): `"HH:mm"`
  - `durationMinutes` (optional, defaults to `60`): `60`–`240`
  - `courtName` (optional): matches a specific court or court type (`VIP`, `Indoor`, `Outdoor`) to include its `Surcharge`.
- **Returned Fields**:
  - Standard rate (`RateStandard`), peak rate (`RatePeak`), VIP surcharge (`VipSurcharge`), member tier discounts (`SilverDiscount`, `GoldDiscount`, `VipDiscount`), segmented quote breakdown (`standardMinutes`, `peakMinutes`, `standardAmountVnd`, `peakAmountVnd`, `surchargeAmountVnd`, `estimatedTotalVnd`), and PayPal Sandbox conversion rate (`VndPerUsd`).

### Tool 4: `search_proshop_products`
- **Source Services & Tables**:
  - `PickleballContext.TbProducts` (`Include(p => p.CategoryProduct)`, `AsNoTracking()`)
  - Automatically excludes internal slot placeholder items (`Title.Trim().ToLower() != "slot"`).
- **Parameters**:
  - `query` (optional): keyword matching product title, category, or description.
  - `category` (optional): category filter.
- **Returned Fields**:
  - Up to 8 matching products with `productId`, `title`, `category`, `priceVnd` (uses `PriceSale` when active, otherwise `Price`), `inStockQty`, `isAvailable`, `description`, and `/Product` link.

### Tool 5: `get_website_guide`
- **Source Services & Tables**:
  - `ClubSettingsService.GetSettingsAsync(PickleballContext)`
  - `PayPalOptions` (`VndPerUsd`)
- **Parameters**:
  - `topic`: `"booking"`, `"qr_checkin"`, `"cancel"`, `"proshop"`, `"payment_paypal"`, `"profile"`, or `"general"`.
- **Returned Fields**:
  - Step-by-step verified instructions and routes (`/Booking`, `/Lookup`, `/Product`, `/Payment/PayPalReturn`, `/Account/Profile`, `/Account/Login`), explicitly labeling PayPal as **PayPal Sandbox (USD Test Mode)**.

### Tool 6: `get_my_bookings` (Protected Personal Data)
- **Source Services & Tables**:
  - `PickleballContext.TbBookings` (`Include(b => b.Court)`, `AsNoTracking()`)
  - `QrTokenService.VerifyGuestAccessToken(rawToken, storedHash)`
- **Authorization Rules**:
  1. **Authenticated Member (`actor.IsAuthenticated && actor.AccountId > 0`)**: Queries ONLY bookings where `b.AccountId == actor.AccountId`. Cross-account booking codes return `found = false`.
  2. **Guest with Verified Token**: Requires both `bookingCode` and a valid `GuestAccessToken` verified against `b.AccessTokenHash` via constant-time `QrTokenService.VerifyGuestAccessToken`.
  3. **Unauthenticated / Unverified Caller**: Returns `authorized = false`, `requiresAuthenticationOrLookup = true`, and directs the user to `/Account/Login` or `/Lookup` without exposing any booking record.
