# SPIKE OS — Gemini AI Mascot Assistant Implementation Plan

**Date:** 2026-11-22  
**Branch:** `feat/paypal-sandbox`  
**Status:** **COMPLETED & VERIFIED (75/75 xUnit Tests + Live Google Gemini Playwright E2E)**  
**Target Models (Verified Live via Google Generative Language API v1beta):** `gemini-3.5-flash-lite` (Primary) / `gemini-3.1-flash-lite` (Automatic Fallback)  

---

## 1. Phase 1 Audit Summary

1. **Existing Floating Mascot (`wwwroot/js/mascot-assistant.js`, `wwwroot/css/components/mascot-assistant.css`):**
   - Renders `#spikeFloatingMascot` in `Views/Shared/_Layout.cshtml` using two overlapping PNG layers (`.spk-mascot-layer-a`, `.spk-mascot-layer-b`) for smooth `220ms` crossfades across 6 normalized `512×512` transparent poses in `wwwroot/assets/assistant/`:
     - `01_idle.png` (`idle`)
     - `02_wave.png` (`wave`)
     - `03_thinking.png` (`thinking`)
     - `04_chat.png` (`chat`)
     - `05_idea.png` (`idea`)
     - `06_jump.png` (`happy` / `jump`)
   - Exposes `window.mascot` (`SpikeMascotController`) with `.play(pose, options)`, `.nextPose()`, `.prevPose()`, `.stop()`, `.setSpeed()`, `.setIdleMotion()`, `prefers-reduced-motion` compliance, and automatic mobile bottom-bar clearance (`.has-mobile-bottom-bar`).
2. **Backend Domain & Data Services:**
   - `ClubSettingsService`: reads `TbCmsConfigs` and produces `ClubSettingsSnapshot` (`OpenTime`, `CloseTime`, `PeakStart`, `BaseCourtPrice`, `PeakSurchargePerHour`, `PendingHoldMinutes`, `CancelCutoffMinutes`, `CheckInEarlyMinutes`, `Hotline`, `PricingVersion`).
   - `PricingService`: calculates minute-accurate standard vs peak segmented quotes (`CalculateQuote`) and member tier discounts (`ResolveMemberTier`, `GetVerifiedAccountNetPaidSpendAsync`).
   - `BookingService`: provides authoritative Vietnam timezone (`GetVietnamNow()`), active pending hold checks (`IsPendingHoldActive`), overlap detection (`RangesOverlap`), and phone normalization.
   - `QrTokenService`: provides `VerifyGuestAccessToken(rawToken, storedSha256Hex)` using constant-time comparison for guest booking ownership verification.
   - `PayPalOptions`: provides `VndPerUsd` Sandbox test exchange rate and `ConvertVndToUsd`.
3. **Security & Pipeline (`Program.cs`):**
   - Cookie authentication (`spike_auth`), CSRF (`RequestVerificationToken` header / `spike_csrf` cookie), partitioned `RateLimiter`, `IMemoryCache`, `IHttpClientFactory`, and `CleanSpikeConsoleFormatter` with `CorrelationContext`.

---

## 2. Implementation Architecture

### 2.1 Configuration & Gemini HTTP Client
- `Configuration/GeminiOptions.cs`:
  - `Enabled` (default `true`)
  - `ApiKey` (loaded from `.NET User Secrets` `Gemini:ApiKey` or `GEMINI_API_KEY` env var; never logged or exposed to client)
  - `Model` (default `"gemini-3.5-flash-lite"`, configurable via `Gemini:Model`, with `"gemini-3.1-flash-lite"` automatic fallback)
  - `BaseUrl` (default `"https://generativelanguage.googleapis.com/v1beta"`)
  - `MaxOutputTokens` (default `512`)
  - `TimeoutSeconds` (default `20`)
  - `MaxInputChars` (default `500`)
  - `MaxConversationTurns` (default `8`)
  - `MaxConcurrentRequests` (default `6`)
- `Services/IGeminiClient.cs` & `Services/GeminiClient.cs`:
  - Typed HTTP client calling `{BaseUrl}/models/{Model}:generateContent` with `x-goog-api-key` header.
  - Supports structured `systemInstruction`, multi-turn `contents`, `tools` (`functionDeclarations`), and `functionResponse` tool-call loop (bounded to max 3 tool rounds per turn).

### 2.2 Read-Only Allowlisted Tool Layer (`Services/SpikeAssistantToolService.cs`)
Exposes 6 strictly validated, read-only tools returning sanitized DTOs:
1. `get_club_info`: club name, address, hotline, opening hours (`OpenTime`–`CloseTime`), peak hours (`PeakStart`–`CloseTime`), active court count & types, and cancellation/check-in rules from `ClubSettingsService` + `TbCourts`.
2. `check_court_availability`: checks live court availability for a date (`yyyy-MM-dd`, relative `"today"` / `"tomorrow"` resolved via `BookingService.GetVietnamNow()`), optional start time (`HH:mm`), and duration (`60..240` mins), respecting active bookings, unexpired `pending` holds (`BookingService.IsPendingHoldActive`), and `TbCourtLocks`.
3. `get_pricing_quote`: computes indicative or court-specific price breakdowns using `PricingService.CalculateQuote`, including standard rate, peak rate, VIP surcharge, member tier discount rules, and PayPal Sandbox USD conversion note.
4. `search_proshop_products`: searches active `TbProducts` (excluding internal `"slot"` items), returning title, category, VND price, stock status (`UnitInStock`), and link `/Product`.
5. `get_website_guide`: returns verified step-by-step instructions and routes (`/Booking`, `/Lookup`, `/Product`, `/Account/Profile`, `/Payment/PayPalReturn`) for booking, QR check-in, cancellation rules, profile updates, and PayPal Sandbox testing.
6. `get_my_bookings`: returns personal booking status ONLY when:
   - the user is authenticated (`Function.IsLogin()`), querying strictly `Where(b => b.AccountId == currentAccountId)`, OR
   - a specific `bookingCode` is accompanied by a valid `guestAccessToken` verified via `QrTokenService.VerifyGuestAccessToken`.
   - Never trusts unverified model arguments or exposes other customers' bookings.

### 2.3 Assistant Orchestrator & Conversation Memory (`Services/IAiAssistantService.cs`, `Services/GeminiAssistantService.cs`)
- Partitioned conversation state in `IMemoryCache` keyed by `"{authenticatedAccountId || sessionActorId}:{conversationId}"` (30-minute sliding expiration, max 8 turns) so conversations cannot leak across accounts or sessions.
- Deduplicates rapid identical in-flight requests and enforces a `SemaphoreSlim` concurrency cap.
- Maps response outcomes to mascot animation poses (`wave`, `thinking`, `chat`, `idea`, `happy`, `idle`).

### 2.4 Controller & Frontend Widget (`Controllers/AssistantController.cs`, `Views/Shared/_Layout.cshtml`, `wwwroot/js/mascot-assistant.js`, `wwwroot/css/components/mascot-assistant.css`)
- `POST /api/assistant/chat` (rate-limited via `"AssistantLimiter"`, CSRF-protected, returns `{ ok, reply, pose, suggestedQuestions, links, conversationId }`).
- `POST /api/assistant/reset` (clears current user's conversation history).
- Floating chat panel mounted directly above `#spikeFloatingMascot` matching the Spike OS Soft Green design system (`#2E8B57`, `#6BCB77`, `#EAF7EE`), with privacy disclosure, suggested chips, typing indicator, error/retry UI, and synchronized mascot pose transitions.
