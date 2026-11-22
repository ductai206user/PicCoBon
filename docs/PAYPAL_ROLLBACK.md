# PayPal Sandbox Integration Rollback Procedure — Spike OS

## 1. Emergency Instant Disable (Zero Downtime / Zero Code Change)

To immediately disable PayPal Checkout without restarting deployments or dropping tables:

1. Set exchange rate to `0` (or unset credentials):
   ```powershell
   dotnet user-secrets set "PayPal:VndPerUsd" "0"
   ```
   (or set environment variable `PayPal__VndPerUsd=0`).
2. `GET /api/payments/paypal/config` returns `enabled: false`, disabling the PayPal Sandbox option across Booking, Pro Shop, Lookup, and Profile UI.
3. Server endpoints (`POST /api/bookings` with `paymentMethod="paypal"` and `POST /api/payments/paypal/create-order`) fail closed with HTTP `503`.
4. Existing cash and bank transfer flows continue unaffected.

## 2. Database Migration Rollback

Migration `20261119200000_V5_PayPalSandboxIntegration` is **100% additive**:
- Adds `dbo.tb_PayPalTransaction` table + constraints/filtered unique indexes.
- Adds no destructive column drops or data rewrites to `tb_Booking`, `tb_Order`, or `tb_PaymentTransaction`.

If rolling back on an isolated test or staging DB when no PayPal rows are needed:

```powershell
# Roll back EF migration to previous snapshot:
dotnet ef database update 20261118150000_V4_SecurityAuditLedgerAndQrPersistence --connection "<TARGET_CONNECTION_STRING>"
```

## 3. Code Rollback (Git)

The PayPal Sandbox integration lives cleanly on branch `feat/paypal-sandbox` branched from `feature/spike-os-redesign` (`9d1e65f`):

```powershell
# Inspect commits on recovery branch:
git log --oneline -n 10

# Revert back cleanly without losing pre-PayPal state:
git checkout feature/spike-os-redesign
```

Note: Never run rollback SQL (`DROP TABLE dbo.tb_PayPalTransaction`) on any live/staging database until all `Completed` / `CompensationRefundRequired` PayPal capture records are exported and reconciled.
