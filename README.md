# UIAMovie — Movie & TV Streaming Platform (REST API)

A RESTful API backend for a Netflix-style streaming platform, built with ASP.NET Core. It covers account security (email OTP, 2FA, rotating refresh tokens in HttpOnly cookies), movie and TV catalogue management with TMDB import, Cloudinary video hosting, admin-configurable Premium plans (including free trials) paid through VNPay or SePay (with an admin revenue dashboard and Excel export), ad serving with a YouTube-style random scheduler, real-time notifications over SignalR, an AI movie assistant, and Redis caching.

The React frontend (Vite, deployed on Vercel) lives in a separate repository and talks to this API over HTTPS.

---

## Table of Contents

- [Features](#features)
- [Tech Stack](#tech-stack)
- [Architecture Overview](#architecture-overview)
- [Authentication Flow](#authentication-flow)
- [Token Revocation](#token-revocation)
- [Subscription Plans](#subscription-plans)
- [Payments (VNPay & SePay)](#payments-vnpay--sepay)
- [Advertising & Ad Scheduling](#advertising--ad-scheduling)
- [API Endpoints](#api-endpoints)
- [Security Hardening](#security-hardening)
- [Installation & Setup](#installation--setup)
- [Configuration](#configuration)
- [Usage](#usage)
- [Deployment](#deployment)
- [Folder Structure](#folder-structure)
- [Git Workflow](#git-workflow)
- [Roadmap](#roadmap)
- [License](#license)

---

## Features

### Authentication & Security
- Registration confirmed by an email OTP (pending sign-ups live in Redis, not in the database)
- Duplicate **email and username** are rejected up front (before the OTP is sent) and checked again when the account is created, so a user never types a valid OTP only to hit a database error. Usernames are trimmed, 3–50 characters, and may contain only letters, digits and `_` (enforced on register and on profile / admin updates). Emails are limited to 254 characters
- Password confirmation (`confirmPassword`) is compared on register and on reset-password; request DTOs carry `[Required]` / `[EmailAddress]` / length validation
- JWT access token (lifetime set by `Jwt:AccessTokenMinutes`, default 15, capped at 60) + opaque refresh token (7 days). `expiresIn` returned to the client comes from the same setting, so the frontend never trusts a wrong expiry
- **Refresh token is delivered only through an `HttpOnly; Secure` cookie** — it is never present in a JSON body, so JavaScript (and therefore XSS) cannot read it
- Refresh token **rotation**; only a SHA-256 hash of each refresh token is stored in the database
- 30-second **grace window** after a rotation so several browser tabs (or a lost response) never log the user out
- Banned or deleted accounts cannot refresh
- **Immediate access-token revocation** when an account is banned, deleted, changes role, or changes / resets its password (see [Token Revocation](#token-revocation))
- Password policy shared by register, reset and change-password (8+ characters, at most 72 bytes because BCrypt truncates beyond that); on reset the new password is validated *before* the OTP is consumed, so a weak password never burns the code
- Case-insensitive, trimmed email handling: `A@x.com` and `a@x.com` are the same account
- Two-factor authentication via email OTP (enable / disable with OTP confirmation)
- Forgot / reset password via email OTP; a reset or a password change revokes every session and every live access token
- OTP brute-force protection (max 5 attempts per 15 minutes) and constant-time OTP comparison
- Atomic OTP / reset-email resend cooldown (60 s) built on Redis `INCR`, so parallel requests cannot trigger two emails
- Consistent user payload: `/me`, admin lists and login responses share one mapper; `subscriptionType` is `null` when the user has no active paid plan (free or expired)
- Dummy BCrypt verification when the user does not exist (no timing-based user enumeration)
- Role-based access control (`Admin` / `User`)
- Rate limiting per policy (see [Security Hardening](#security-hardening))
- Background email queue with retry and bounded concurrency — requests never wait for SMTP
- Background worker removes expired `UserSession` rows every 6 hours
- Admin user list filters, sorts and paginates in the database and loads only the displayed columns (no password hashes or 2FA secrets in memory)
- Consistent error shape: validation errors, controller errors and the global exception middleware all return camelCase JSON with a `message` field

### Movies & TV Shows
- CRUD for movies, TV shows, seasons and episodes (Admin only)
- TMDB import — metadata, cast, directors, trailers and images in one call; genre sync
- Video upload to Cloudinary
- Subtitles for movies and episodes
- Search by title or actor; filtering by genre, rating, release date and country; sorting and pagination
- Trending content, person (cast / director) pages

### Playback, Premium & Payments
- `PlaybackService` decides who may watch what; the paywall never returns a playback URL when the viewer is not entitled
- **Admin-configurable plans** (`SubscriptionPlan` table + `AdminPlanController`): price, original price, duration, features, sort order, "most popular" (only one at a time), sale window (`StartsAt` / `EndsAt`, UTC), purchase limit per user, e-mail domain whitelist (for example a student plan) and a trial flag. Plans are switched on or off, never deleted, because orders reference them by `PlanId` string (no FK) and snapshot their name, price and days (see [Subscription Plans](#subscription-plans))
- **Free trial plans:** a plan priced at 0 is always a trial and `MaxPerUser` defaults to 1. `create-order` activates it immediately (order `provider = "free"`, `amount = 0`, `paymentMethod = "none"`), so it never goes through a gateway. That successful order is the proof that the trial was used
- **Eligibility** is enforced server-side in `create-order` and exposed per plan in `GET /payments/plans` for signed-in users (`isEligible`, `ineligibleReason`): plan on sale, purchase limit, trial only without active Premium, e-mail domain
- Premium paid through **VNPay** (redirect) or **SePay** (form post). Callbacks are verified (VNPay: HMAC-SHA512 signature; SePay: secret header + order cross-check), idempotent and safe against the browser-return / IPN race (see [Payments (VNPay & SePay)](#payments-vnpay--sepay))
- Abandoned `pending` orders are moved to `expired` by `PaymentExpiryWorker` (data is never deleted)
- Users can view their payment history and subscription status, and cancel their own Premium (access ends **immediately**; the unused time is **not refunded**; a cancelled trial cannot be taken again)
- `subscription-status` also returns `planId` / `planName` (taken from `UserSubscription.LastPaymentOrderId`), so the client can mark the current plan. Both are `null` for admin-granted Premium
- Buying again while Premium is active is allowed and **extends** the current expiry (renewal); the API does not block `create-order` for active subscribers (only trial plans are blocked)
- Plans advertise whatever feature strings the admin enters. There is no refund policy
- `Premium` can only be set through payment, a free trial or `admin/grant-subscription`, so every Premium user has a `UserSubscription` row (what `SubscriptionChecker` reads)
- Payment receipt e-mails are queued after a successful activation (paid or trial); a mail failure never fails the activation
- Admin tools to grant or revoke subscriptions, plus a revenue dashboard (summary, chart, by-plan, order list) whose dates are computed in **Vietnam time (GMT+7)** and filtered in the database, and an **Excel export** (3 sheets) with an audit log line
- Watch history with progress, heartbeat-based view tracking and view statistics
- **Advertisements** for Free users: pre-roll / mid-roll / post-roll, either **Fixed** (the slots an admin attached) or **Random** (YouTube-style schedule built per view, front-loaded). Admins switch the mode and tune every parameter at runtime and can preview a sample schedule (see [Advertising & Ad Scheduling](#advertising--ad-scheduling))

### Social & Realtime
- Reviews and ratings (1–10) with statistics and moderation
- Favourites list
- Real-time notifications through SignalR (`/hubs/notifications`)
- AI assistant (Groq) with movie / TV retrieval, comparison and review-summary tools, rate limited per user

### Performance
- Redis cache (Upstash) with `GetOrSetAsync` and pipeline batch invalidation
- `AsNoTracking()` on read queries
- In-memory cache for lightweight content metadata used by heartbeats

---

## Tech Stack

| Category | Technology |
|---|---|
| Runtime | ASP.NET Core (see `TargetFramework` in the `.csproj`) |
| ORM / Database | Entity Framework Core + PostgreSQL |
| Cache | Redis (Upstash) via StackExchange.Redis |
| Authentication | JWT Bearer + BCrypt, refresh token in HttpOnly cookie |
| Realtime | SignalR |
| Email | MailKit / SMTP (background queue) |
| Video & images | Cloudinary |
| Metadata | TMDB REST API v3 |
| Payments | VNPay (HMAC-SHA512 signed callbacks) and SePay (secret-header IPN) |
| Excel export | ClosedXML (`UIAMovie.API` only) |
| AI | Groq (behind `IAiProvider`) |
| Validation | FluentValidation + DataAnnotations |
| TOTP helper | OtpNet (`TwoFactorAuthProvider`, not yet wired into the login flow) |

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────┐
│           React SPA (Vercel)  ──  HTTPS / WSS            │
└───────────────────────────┬──────────────────────────────┘
                            │  Authorization: Bearer <access token>
                            │  Cookie: refreshToken (HttpOnly, only on /api/auth/*)
┌───────────────────────────▼──────────────────────────────┐
│                   ASP.NET Core API (Render)               │
│  ForwardedHeaders → HTTPS → Security headers → Exception  │
│  middleware → CORS → Authentication → Rate limiter → Authz│
│                                                           │
│  Controllers │ SignalR Hubs │ Validators                  │
│        │                                                  │
│  Application Services (Auth, Movie, TvShow, Playback,     │
│  Payment, SubscriptionPlan, Notification, AI, ...)        │
│        │                       │                          │
│  Redis cache (Upstash)   Generic / specialised repos      │
│                          (EF Core + PostgreSQL)           │
└────────┬──────────────┬───────────────┬──────────────────┘
         │              │               │
   Cloudinary        TMDB API   VNPay / SePay / Groq / SMTP
```

**Layer responsibilities**

The solution follows Clean Architecture: dependencies point inward (API → Application → Domain; Infrastructure implements Application's interfaces).

- **Controllers / Hubs** — HTTP routing, request and response shaping, authorization and rate-limit attributes
- **Services** — business logic, cache management, orchestration
- **Repositories** — data access via EF Core (`Repository<T>` plus specialised repositories such as `IUserRepository`)
- **Middleware** — security headers, global exception handling
- **Infrastructure** — Redis, Cloudinary, JWT generator, email queue, TMDB / Groq / VNPay / SePay clients

**Where "security" code lives**

| Layer | Holds | Rule of thumb |
|---|---|---|
| `Application/Security` | *Policy*: `PasswordPolicy`, `TokenRevocationService` | Uses only interfaces and domain entities; stays valid if Redis or JWT is swapped out |
| `Infrastructure/Security` | *Mechanism*: `JwtTokenGenerator`, `TwoFactorAuthProvider` | Depends on a specific library (JWT, OtpNet, ...) |

---

## Authentication Flow

```
Login ─────────────► 200  { accessToken, expiresIn, user }
                          Set-Cookie: refreshToken=…; HttpOnly; Secure; SameSite=None; Path=/api/auth

(2FA enabled)  Login ► 200 { requiresOtp: true, userId }  ──►  POST /auth/otp/verify ──► same as above

Access token expires / page reload
   └─► POST /auth/refresh-token   (cookie sent automatically)
         ├─ valid        → new access token + rotated cookie
         ├─ just rotated → (≤ 30 s) same new session, no forced logout
         └─ invalid      → 401 and the cookie is deleted
```

Notes for the frontend:

- Keep the access token **in memory only** (module variable). Do not write it to `localStorage`.
- On start-up and on `401`, call `/auth/refresh-token` once (single-flight) and retry the original request.
- `Origin` of `/auth/refresh-token` and `/auth/logout` calls must be in `Cors:AllowedOrigins`, otherwise the API answers `403`.
- Because the cookie `Path` is `/api/auth`, a reverse proxy must forward `/api/*` unchanged.
- After a password change, ban, or role change the API invalidates existing access tokens. A `401` followed by a failed refresh means "sign in again"; after `POST /user/me/change-password` the frontend should send the user to the login page.
- Error responses always look like `{ "success": false, "message": "...", "statusCode": 400 }`, so `response.data.message` is always readable.

---

## Token Revocation

Access tokens are stateless JWTs, so they cannot be deleted. Instead, the API stores a **revocation mark** (Unix seconds) per user in Redis under `user:revoked:{userId}`. `OnTokenValidated` rejects any token whose `iat` is **less than or equal to** the mark. Tokens issued *after* the mark (a fresh login, a refresh, an unban) stay valid.

| Event | Access tokens | Refresh tokens / sessions |
|---|---|---|
| Ban user | Revoked | All deleted |
| Delete user | Revoked | Cascade-deleted |
| Change role | Revoked (client refreshes to get the new role) | Kept |
| Change password | Revoked | All deleted |
| Reset password (OTP) | Revoked | All deleted |
| Unban user | Unchanged (old mark expires; new logins work) | — |

Design notes:

- The mark's TTL is `AccessTokenLifetime + 1 h`, derived from configuration, so changing token lifetime cannot silently break revocation. Because the lifetime is capped at 60 minutes, the mark lives at most 2 hours.
- `<=` is deliberate: both `iat` and the mark have one-second resolution, and `<` would let through a token issued in the same second as the ban. The flip side is that a token issued in the very same second *after* the mark is also rejected; the client simply refreshes again.
- Revoking all sessions writes the mark **before** deleting refresh-token rows, so a failure while deleting sessions can never bring old tokens back to life.
- **Fail-open:** if Redis is unreachable, the check passes instead of taking the whole API down. A failed *write* of the mark is likewise not fatal, but then an old access token stays usable until it expires (at most 60 minutes), while refreshing is still blocked because the sessions are deleted. Switch `IsRevokedAsync` to fail-closed if your threat model prefers that.
- Implemented by `ITokenRevocationService` (Application) and injected into `AuthService`, `UserService` and the JWT bearer events.

---

## Subscription Plans

Plans are rows in the `SubscriptionPlans` table (entity `SubscriptionPlan`), managed through `SubscriptionPlanService` and `AdminPlanController` (`/api/admin/plans`, Admin only) and consumed by `PaymentService`. Nothing is hard-coded any more: the frontend renders what `GET /api/payments/plans` returns.

### Fields

| Field | Meaning |
|---|---|
| `PlanId` | Unique slug (`monthly_premium`, `student_monthly`, `trial_7d`). Lower-cased, **immutable** after creation. Orders reference it by string, not by FK |
| `Name`, `Description` | Display text |
| `PriceVnd` | Price in VND. `0` = free trial plan |
| `OriginalPriceVnd` | Optional strikethrough price (must be greater than `PriceVnd`) |
| `DurationDays` | 1–3650 |
| `IsMostPopular` | "Most popular" badge. Saving a plan with it set clears it on every other plan |
| `IsActive`, `SortOrder` | Sales switch and display order (then by price) |
| `FeaturesJson` | JSON array of feature strings, exposed as `features` |
| `IsTrial`, `MaxPerUser` | Trial flag and the maximum number of **successful** purchases per user (`null` = unlimited) |
| `AllowedEmailDomains` | CSV of allowed e-mail domains (`edu.vn,ac.vn`); empty = anyone |
| `StartsAt`, `EndsAt` | Sale window in UTC; `null` = open ended |

### Rules

| Rule | Detail |
|---|---|
| Plan id | `^[a-z0-9_]{3,50}$`, trimmed and lower-cased, must not already exist |
| Price | `0` (trial) or at least 10,000đ. Adjust the minimum to the real limits of VNPay / SePay |
| Original price | If set, must be greater than the price |
| Trial | Price 0 ⇒ `IsTrial = true` and `MaxPerUser` defaults to 1, so free Premium can never be unlimited |
| On sale | `IsActive` and `StartsAt ≤ now < EndsAt` |
| Sale window | `EndsAt` must be after `StartsAt` |
| E-mail domains | Normalised: `@` / `.` trimmed, lower-cased, de-duplicated. A user's host must equal a domain or end with `.domain` |
| Purchase limit | Counts `success` orders per `PlanId` (free-trial orders count too) |
| Editing | Does not change past orders: each `PaymentOrder` stores `PlanName`, `DurationDays` and `Amount` at purchase time |
| Deleting | Not supported. Switch the plan off instead, to keep the order history |

### Eligibility (`CheckEligibility`)

Checked in `create-order` (the source of truth) and reported per plan by `GET /payments/plans` when a token is sent. The first failing rule wins:

1. The plan is not on sale → "Gói này hiện không còn được bán."
2. The purchase limit is reached → "Bạn đã dùng thử gói này rồi." (trial) or "Bạn đã dùng hết số lần được mua gói này."
3. A trial plan while the user has active Premium → "Dùng thử chỉ áp dụng khi bạn chưa có gói Premium đang hoạt động."
4. The e-mail domain is not allowed → "Gói này chỉ dành cho email thuộc: …"

Anonymous requests always get `isEligible = true`; `create-order` checks again.

### Endpoints (`/api/admin/plans`)

| Method | Endpoint | Description |
|---|---|---|
| GET | `/` | All plans, including switched-off ones |
| POST | `/` | Create a plan (`UpsertPlanDTO`) |
| PUT | `/{id}` | Update a plan (`id` is the Guid, **not** `planId`; `planId` cannot change) |
| PATCH | `/{id}/active` | Body `{ "isActive": true \| false }` — switch sales on / off |

Validation errors come back as `400 { "message": "…" }`; unexpected errors as `500`.

### Manual grant

`POST /api/payments/admin/grant-subscription` takes `{ userId, planId, expiredAt }`. The plan only has to exist (a switched-off plan is fine, for example a trial given as a gift) and `expiredAt` must be in the future. The expiry is set exactly as given, it is **not** added to the remaining time, and the subscription is no longer linked to any order (`LastPaymentOrderId = null`).

### Migration & first plans

The `SubscriptionPlans` table needs a migration. `/payments/plans` returns an empty list until at least one plan exists, so create your first plans (for example `monthly_premium` and `yearly_premium`) through the admin UI or `POST /api/admin/plans`, or seed them in the migration.

```bash
dotnet ef migrations add AddSubscriptionPlans --project UIAMovie.Infrastructure --startup-project UIAMovie.API
dotnet ef database update --project UIAMovie.Infrastructure --startup-project UIAMovie.API
```

---

## Payments (VNPay & SePay)

Both gateways share one order model and one confirmation core (`ConfirmOrderAsync`). The gateway is chosen by `paymentProvider` (`"vnpay"` | `"sepay"`) in `create-order`. Plans priced at 0 skip the gateways entirely (see below).

### Flow

```
1. POST /api/payments/create-order { planId, paymentProvider }
      → plan must exist, be on sale and the user must be eligible (see Subscription Plans)
      → price 0  : ActivateFreePlanAsync — order (provider=free, status=success) + Premium in one SaveChanges
                   → { paymentMethod: "none", paymentUrl: "" }  FE: no redirect, go to /payment/result
      → price > 0: PaymentOrder (status=pending, expires in 15 min)
                   the user's other still-valid pending orders become `cancelled` ("Thay bằng đơn mới")
        → VNPay : { paymentMethod: "redirect",  paymentUrl }       FE: location.href = paymentUrl
        → SePay : { paymentMethod: "form_post", paymentUrl, formFields }
                                                                   FE: POST a hidden form, fields in the SAME order

2. User pays on the gateway.
      VNPay → GET  /api/payments/vnpay/ipn             (server-to-server)
            + GET  /api/payments/vnpay/return          (user's browser)      both idempotent
      SePay → POST /api/payments/sepay/ipn             (server-to-server, header X-Secret-Key)
            + GET  /api/payments/sepay/return/{orderCode}/{success|error|cancel}   (browser)

3. ConfirmOrderAsync (shared)
      Redis lock  payment:ipn:{orderCode}  (atomic INCR, 30 s)    only one request proceeds
        → load order; the order's provider must equal the callback's provider
        → reject if no longer pending                              idempotent (late successes excepted)
        → check amount (VND)
        → ONE SaveChanges: order + UserSubscription + User.SubscriptionType
        → invalidate user / subscription caches, queue the receipt e-mail

4. The return endpoints redirect to {Frontend}/payment/result?status=…&orderCode=…
```

`status` in that URL is for display only. The frontend confirms the real result through `GET /api/payments/history` (looks the order up by `orderCode`) or `GET /api/payments/subscription-status`.

### Free (trial) plans

- `ActivateFreePlanAsync` takes an atomic Redis lock `payment:free:{userId}:{planId}` (30 s) against double clicks, creates the order as `success` (`Provider = "free"`, `Amount = 0`) and upgrades the subscription in a single `SaveChanges`.
- The order is the evidence for `MaxPerUser`: cancelling the trial later does **not** give it back.
- Free orders never go through `ConfirmOrderAsync` (their provider matches no gateway) and are excluded from revenue figures.

### VNPay

- **Signature** is computed over *every* `vnp_*` parameter in the query (except `vnp_SecureHash` / `vnp_SecureHashType`, empty values skipped, sorted ordinally, `UrlEncode`d, HMAC-SHA512) and compared in constant time. It does not depend on the DTO, so extra parameters from VNPay never break verification.
- Success only if `vnp_ResponseCode == "00"` **and** `vnp_TransactionStatus == "00"`; `vnp_Amount / 100` must equal the order amount.
- **Time zone:** `vnp_CreateDate` / `vnp_ExpireDate` are always GMT+7, regardless of the server's time zone.

IPN answers (HTTP 200 with `{ "RspCode", "Message" }` for every outcome):

| RspCode | Meaning |
|---|---|
| `00` | Confirmed |
| `01` | Order not found |
| `02` | Order already confirmed |
| `04` | Invalid amount |
| `97` | Invalid signature |
| `99` | Busy (another request holds the lock) or unknown error — VNPay will retry |

### SePay

- The IPN has **no signature**; it is authenticated by the `X-Secret-Key` header, checked *before* touching the lock or the database.
- Only `notification_type = ORDER_PAID` with `order_status = CAPTURED` and `transaction_status = APPROVED` confirms an order. Any other status is ignored (never marks the order `failed`, so a valid payment arriving later still works). `TRANSACTION_VOID` is only logged for manual review — it does not revoke the plan.
- `order_amount` is real VND (no ×100) and may arrive as a string such as `"50000.00"`; a non-integer amount is rejected as an amount mismatch. The invoice number is our `orderCode`, and the order's provider must be `sepay`.
- URLs handed to SePay use a path segment (`/sepay/return/{orderCode}/{result}`) instead of a query string so the signed values contain no `&`.

### Order lifecycle

`pending` → `success` | `failed` | `expired` | `cancelled`.

- Listings and statistics show a `pending` order past `ExpiredAt` as **`expired`** (a display rule only — the row is not changed, so a late callback is still processed).
- `PaymentExpiryWorker` runs every 10 minutes and moves `pending` orders older than `ExpiredAt + 30 min` (grace) to `expired` with a conditional `UPDATE`, which is safe when several instances run it at once. It never deletes data. A callback that confirms an order in the same moment wins, because the `UPDATE` only touches rows that are still `pending`.
- `failed` means the gateway reported an error, the amount did not match, or the gateway could not create the payment.
- `cancelled` means the order was **replaced by a newer one**: `create-order` marks the user's other still-valid `pending` orders as `cancelled` (reason "Thay bằng đơn mới") in the same `SaveChanges` that inserts the new order, so opening and abandoning the gateway never piles up `pending` rows. Cancelled orders appear in `totalOrders` but not in `successRate`.
- **Late payments are honoured:** a *successful* callback for an `expired` or `cancelled` order (worker timeout, superseded order, slow bank transfer) still confirms the order, grants Premium and clears the failure reason; a *failed* callback for such an order is ignored. Money that left the customer's account is never left without a plan.
- **Order code:** `ORD-yyyyMMdd-XXXXXXXX` (8 random hex characters from a cryptographic RNG), checked for existence before use; `PaymentOrder.OrderCode` also has a unique index as the last line of defence.

### Database indexes & migration

`MovieDbContext` defines, for payments: unique `PaymentOrder.OrderCode`; `PaymentOrder (UserId, Status)`, `(Status, ExpiredAt)`, `PaidAt`, `CreatedAt` (expiry worker, "one open order per user", revenue queries); and a **unique** `UserSubscription.UserId`. For plans, see [Subscription Plans](#subscription-plans) (a unique index on `SubscriptionPlans.PlanId` is recommended, see [Roadmap](#roadmap)).

Before applying the migration that makes `UserSubscription.UserId` unique, make sure no user has two rows (keep the one with the latest `ExpiredAt`):

```sql
SELECT "UserId", COUNT(*) FROM "UserSubscriptions" GROUP BY "UserId" HAVING COUNT(*) > 1;
```

```bash
dotnet ef migrations add PaymentIndexes --project UIAMovie.Infrastructure --startup-project UIAMovie.API
dotnet ef database update --project UIAMovie.Infrastructure --startup-project UIAMovie.API
```

### Notes

- **Stacking:** paying while Premium is still active extends from the current expiry date, not from today. The frontend therefore offers "renew" instead of blocking the purchase. A trial plan is the exception: it is refused while Premium is active.
- **Source of truth:** use `GET /payments/subscription-status` (backed by `UserSubscription` and `ExpiredAt`) to decide who is Premium. `User.SubscriptionType` is a convenience copy and is not read by `SubscriptionChecker`. The status is cached for 10 minutes and invalidated on every upgrade, cancel, grant and revoke.
- **Current plan:** `planId` / `planName` come from the order stored in `UserSubscription.LastPaymentOrderId`. A manual grant clears that link, so they are `null` for admin-granted Premium.
- **Cancel:** `DELETE /payments/subscription/me` deletes the subscription (no remaining-time refund, no grace period) and clears the Premium cache.
- **Atomicity:** all repositories share one scoped `DbContext`, so a single `SaveChanges` stores the order, the subscription and the user together. If you ever stop sharing the context, revisit this.
- **Limit of the lock:** the Redis lock only protects within one Redis instance. For absolute safety against double-crediting, add a concurrency token (`xmin` on PostgreSQL) to `PaymentOrder` (see [Roadmap](#roadmap)).
- Production must set `Backend:PublicUrl` and `Frontend:Url` (or `FRONTEND_URL`); the API refuses to guess them outside Development.
- **Duplicate routes break everything:** the admin revenue / order endpoints live only in `AdminRevenueController` (URLs unchanged: `/api/payments/admin/...`). Declaring the same route again in `PaymentController` makes ASP.NET Core throw `AmbiguousMatchException` and every one of those requests returns HTTP 500 before reaching any action.

### Revenue reporting & Excel export

Implemented by `RevenueService` (Application) and `AdminRevenueController` + `RevenueExcelExporter` (API).

- **Time zone:** the database stores UTC; every report day / month is **Vietnam time (GMT+7, no DST)**. `VnTime` converts the VN day boundary to UTC and the database does the filtering, so no table is loaded into memory. `from` / `to` are *dates*, both inclusive, and `from` must not be after `to` (otherwise `400`). API timestamps are UTC.
- **Revenue** is the sum of `success` orders by **paid date**, as the customer paid through the gateway — **before** gateway fees and refunds / voids. Free-trial orders (`provider = "free"`, 0đ) are not counted in the payment metrics.
- **Summary** (`GET /admin/revenue/summary`): `totalRevenue`, `totalOrders`, `successOrders`, `pendingOrders` (not yet expired), `failedOrders` (gateway error / wrong amount only), `expiredOrders`, `cancelledOrders` (replaced by a newer order), `successRate = success / (success + failed + expired)` (pending and cancelled excluded), `averageOrderValue`, `todayRevenue` / `todayOrders`, `currentMonthRevenue` / `currentMonthOrders`, `lastMonthRevenue`, `monthOverMonthGrowth` (**`null`** when last month is 0 — no fake 100 %), `activePremiumUsers`, `expiringIn7Days`, `generatedAt`.
- **Chart** (`GET /admin/revenue/chart`): `groupBy=month&year=…` or `groupBy=day&year=…&month=…`; every day / month is returned (0 when empty) so the chart never has gaps. `year` defaults to the current VN year.
- **By plan** (`GET /admin/revenue/by-plan`): revenue, orders and share per plan, optional `from` / `to`.
- **Orders** (`GET /admin/orders`): filter (`status`, `planId`, `provider`, `from`, `to` by **created date**, `search` by order code or e-mail) and paging run in the database; `pageSize` is clamped to 1–100. `status` is the effective status (a stale `pending` is `expired`; an unknown value is `400`). `provider` accepts `vnpay`, `sepay` or `free`.
- **Export** (`GET /admin/revenue/export?from&to&status&provider&planId`): returns `doanh-thu_<yyyyMMdd>_<yyyyMMdd>.xlsx` (or `doanh-thu_toan-thoi-gian.xlsx`) with three sheets — *Tổng quan*, *Theo gói*, *Đơn hàng*. Without `status` only `success` orders are exported; successful orders are filtered by **paid date**, other statuses by **created date**. More than 50,000 rows → `400` asking to narrow the range. Cells that start with `= + - @` are neutralised against spreadsheet formula injection, and each export writes an audit log line (admin id, range, status, row count) because the file contains customer e-mails.

---

## Advertising & Ad Scheduling

Implemented by `AdService` + `RandomAdPlanner` (Application), `AdScheduleOptions` (Application/Options) and `AdsController` (API). Only Free users receive ads; the player asks for a schedule before playback starts.

### Scheduling modes

| Mode | Behaviour |
|---|---|
| `Fixed` (default) | Plays exactly the admin's **Global Slots** and per-content **Overrides** (an Override at a position replaces the Global Slots at that position) |
| `Random` | Builds a **new schedule for every view** from the ads attached to Global Slots. Content that has an Override (its own, or its show's) keeps its hand-made schedule |

### Content scopes

The player asks for ads by `Movie` or `Episode` only. `TvShow` therefore means **the whole show**:

- A Global Slot with `appliesTo = TvShow` applies to **every episode**; `appliesTo = null` means all formats (movies + episodes)
- An Override of type `TvShow` applies to every episode of that show; an episode's own Override wins over its show's. The player passes the show id as `?parentId=` and the cache key follows the override's source, so removing a show override refreshes all its episodes

### Random algorithm (`RandomAdPlanner`, pure logic — no DB, cache or DI)

1. **Pool** — the distinct ads (with a video URL) found in the merged Global Slots for the content type. An ad only needs to be attached to *any* Global Slot to enter the pool.
2. **Pre-roll (front-loaded)** — the number of opening ads is drawn from `PreRollWeights` (index *i* = weight of "*i* ads"). Default `{5, 40, 40, 15}` → 0 ads 5 %, 1 ad 40 %, 2 ads 40 %, 3 ads 15 %. The count is capped at the pool size so a single ad is never repeated back-to-back.
3. **Mid-roll (everything is a percentage of the video's own duration)** — mid-roll *k* sits near `k × MidRollIntervalRatio` (0.2 → 20 %, 40 %, 60 %, 80 %), moved by a random ± `JitterRatio × interval`. A 2-minute video and a 2-hour video get the same proportions:
   - never closer than `MinMidRollGapRatio` of the duration to the previous mid-roll (or to the start)
   - none inside the last `EndGuardRatio` of the video, and at most `MaxMidRolls`
   - offsets are rounded **up** to whole seconds and kept strictly increasing
   - optional `MinDurationForMidRollSeconds` (default 0 = off) drops mid-rolls for very short videos
   - none when `durationSeconds` is 0 / missing (client values are clamped to 0–24 h)
4. **Post-roll** — appears with probability `PostRollChance`.
5. **Ad picking** — a shuffle-bag over the pool: every ad is used once before any repeats, and the same ad is never picked twice in a row. Every placement gets its **own `SlotId`** because the frontend uses it as the "already played" key for mid-rolls.

The random plan is never cached (each view differs); only the pool (global slots) comes from the cache. `GET /ads/content/...` answers `Cache-Control: no-store`.

### Settings (editable by Admin)

`AdScheduleOptions` is bound from `Ads:Schedule` (defaults below). An admin can override it at runtime through `PUT /api/ads/schedule-settings`; the override is stored in the cache service under `ads:schedule-settings` (TTL 365 days) and falls back to appsettings when absent. Every read and write goes through `Normalized()`, which clamps values (≤ 6 pre-roll weights each 0–1000 and a positive sum, ratio 0–1, jitter 0–0.9, gap ≥ 30 s, max mid-rolls 0–20, post-roll chance 0–1).

> **Persistence note:** with an in-memory cache the override is lost on restart (appsettings applies again). With Redis (Upstash) it survives restarts. To make it durable regardless of the cache, store the options in a table.

### Duration is required for mid-rolls

The player must call `GET /api/ads/content/{contentType}/{contentId}?durationSeconds=<seconds>` (episodes also send `&parentId=<showId>`). The service passes it to the planner; without it Random mode produces pre-/post-roll only.

### Admin preview

`POST /api/ads/schedule-preview` uses the options in the request body (unsaved values) or the saved ones. `contentType` omitted / `null` = **all formats** (movies + episodes). In `Random` mode it runs the real planner; in `Fixed` mode it returns the fixed schedule. The response is `{ mode, durationSeconds, pool: [{ adId, title, durationSeconds }], schedule: { preRoll, midRoll, postRoll } }` — `pool` is the list of ads the scheduler may draw from (empty = nothing will play). Nothing is stored.

```http
POST /api/ads/schedule-preview
Authorization: Bearer <admin-access-token>
Content-Type: application/json

{ "contentType": "Movie", "durationSeconds": 7200, "options": { "mode": "Random", "midRollIntervalRatio": 0.2 } }
```

---

## API Endpoints

### Auth — `/api/auth`

| Method | Endpoint | Auth | Rate limit | Description |
|---|---|---|---|---|
| POST | `/register` | Public | auth-strict | Start registration; sends an OTP |
| POST | `/register/verify-otp` | Public | auth-strict | Confirm the OTP and create the account |
| POST | `/login` | Public | auth-strict | Login; returns access token (+ cookie) or an OTP prompt |
| POST | `/otp/send` | Public | auth-strict | Resend the login OTP |
| POST | `/otp/verify` | Public | auth-strict | Verify the OTP → access token (+ cookie) |
| POST | `/2fa/enable` | User | — | Send an OTP to enable 2FA |
| POST | `/2fa/confirm` | User | — | Confirm the OTP and enable 2FA |
| POST | `/2fa/disable` | User | — | Verify the OTP and disable 2FA |
| POST | `/forgot-password` | Public | auth-strict | Send a reset OTP (always answers the same) |
| POST | `/reset-password` | Public | auth-strict | Reset the password with the OTP; revokes all sessions |
| POST | `/refresh-token` | Cookie | auth-refresh | Rotate the refresh token, issue a new access token |
| POST | `/logout` | Cookie | auth-refresh | Revoke the session of the current refresh token (this device only) and delete the cookie |

### Movies — `/api/movies`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/` | Public | List movies (paginated) |
| GET | `/{id}` | Public | Movie detail with cast, genres, videos |
| GET | `/trending` | Public | Trending movies |
| GET | `/filter` | Public | Filter by genre, rating, country, date |
| GET | `/search` | Public | Search by title |
| GET | `/search/actor` | Public | Search by actor name |
| POST | `/` | Admin | Create movie |
| POST | `/import-tmdb/{tmdbId}` | Admin | Import a movie from TMDB |
| POST | `/{id}/upload-video` | Admin | Upload a video to Cloudinary |
| PUT | `/{id}` | Admin | Update movie |
| DELETE | `/{id}` | Admin | Delete movie |

### Users — `/api/user`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/me` | User | Own profile |
| PUT | `/me` | User | Update own profile (`username`, `avatarUrl`). `subscriptionType` is ignored: plans only change through payment or Admin |
| POST | `/me/change-password` | User | Change password; signs the user out of every device |
| GET | `/` | Admin | List users (search, filter, paginate) |
| GET | `/{id}` | Admin | Get a user |
| PUT | `/{id}` | Admin | Update a user. Username must be unique and match `[A-Za-z0-9_]{3,50}`. `subscriptionType` accepts only `free` / `standard`; Premium values are rejected (use `grant-subscription`) |
| PATCH | `/{id}/role` | Admin | Change role (an admin cannot change their own role) |
| DELETE | `/{id}` | Admin | Delete a user (not yourself) |
| POST | `/{id}/ban` | Admin | Ban a user and revoke all tokens (not yourself). Body: `{ "reason": "…" }` (optional, max 500 chars). There is no `banUntil`; bans last until an unban |
| POST | `/{id}/unban` | Admin | Lift a ban |

### Payments — `/api/payments`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/plans` | Public | Plans currently on sale. With a token, each plan carries `isEligible` / `ineligibleReason` |
| POST | `/create-order` | User | Create an order for `vnpay` (redirect) or `sepay` (form post: `paymentUrl` + `formFields`). A 0đ plan is activated at once and answers `paymentMethod: "none"` (no URL) |
| GET | `/vnpay/ipn` | VNPay | Server-to-server callback (VNPay uses GET) |
| GET | `/vnpay/return` | Public | Browser return; processes the callback idempotently, then redirects to the frontend |
| POST | `/sepay/ipn` | SePay | Server-to-server callback, authenticated by the `X-Secret-Key` header |
| GET | `/sepay/return/{orderCode}/{result}` | Public | Browser return (`success` / `error` / `cancel`), then redirects to the frontend |
| GET | `/history` | User | Own payment history (`status`: `pending` / `success` / `failed` / `expired` / `cancelled`; `provider`: `vnpay` / `sepay` / `free`) |
| GET | `/subscription-status` | User | Current subscription (`isPremium`, `daysRemaining`, `isExpiringSoon`, `planId`, `planName`) |
| DELETE | `/subscription/me` | User | Cancel own Premium immediately (no refund) |
| POST | `/admin/grant-subscription` | Admin | Grant / extend Premium: `{ userId, planId, expiredAt }` (`expiredAt` must be in the future) |
| DELETE | `/admin/subscription/{userId}` | Admin | Revoke Premium |
| GET | `/admin/revenue/summary` | Admin | Totals, today / this month / last month, growth, success rate, active Premium users (Vietnam time) |
| GET | `/admin/revenue/chart` | Admin | `groupBy=month&year=…` or `groupBy=day&year=…&month=…` (`year` 2000–2100, `month` 1–12) |
| GET | `/admin/revenue/by-plan` | Admin | Revenue split per plan, optional `from` / `to` (VN dates, inclusive) |
| GET | `/admin/orders` | Admin | Orders with filter (`status`, `planId`, `provider`, `from`, `to`, `search`), paging in the database |
| GET | `/admin/revenue/export` | Admin | `.xlsx` report (3 sheets); defaults to successful orders; audit-logged |

> The five `/admin/revenue/*` and `/admin/orders` routes are served by `AdminRevenueController`; `grant-subscription` and `subscription/{userId}` stay in `PaymentController`.

### Plans (admin) — `/api/admin/plans`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/` | Admin | All plans, including switched-off ones |
| POST | `/` | Admin | Create a plan |
| PUT | `/{id}` | Admin | Update a plan (Guid `id`; `planId` is immutable) |
| PATCH | `/{id}/active` | Admin | Switch a plan on / off (`{ "isActive": bool }`) |

### Genres — `/api/genres`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/` | Public | All genres |
| GET | `/{id}` | Public | Genre by ID |
| POST | `/` | Admin | Create genre |
| PUT | `/{id}` | Admin | Update genre |
| DELETE | `/{id}` | Admin | Delete genre |
| POST | `/sync-tmdb` | Admin | Sync genres from TMDB |

### Reviews — `/api/ratingreview`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/movies/{movieId}` | Public | Movie reviews (paginated) |
| GET | `/movies/{movieId}/stats` | Public | Rating statistics |
| GET | `/{reviewId}` | Public | Single review |
| POST | `/` | User | Create review |
| PUT | `/{reviewId}` | User | Update own review |
| DELETE | `/{reviewId}` | User | Delete own review |
| GET | `/my` | User | Own reviews |
| GET | `/check/{movieId}` | User | Check whether the movie was reviewed |
| DELETE | `/admin/{reviewId}` | Admin | Remove any review |

### Ads — `/api/ads`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/content/{contentType}/{contentId}?durationSeconds=&parentId=` | Public | Player: ads grouped as `preRoll` / `midRoll` / `postRoll` for one view. Fixed or Random per the current mode; `no-store` |
| GET | `/` | Admin | Paged ad list (filters) |
| GET | `/{id}` | Admin | Ad detail with its global slots |
| POST | `/` | Admin | Create an ad (`multipart/form-data`: video upload or external URL, brand image) |
| PUT | `/{id}` | Admin | Update an ad / replace the video |
| DELETE | `/{id}` | Admin | Delete an ad and its Cloudinary files |
| POST | `/{adId}/global-slots` | Admin | Attach an ad to all content or one content type at a position |
| PATCH | `/global-slots/{slotId}` | Admin | Change position / scope / active state |
| DELETE | `/global-slots/{slotId}` | Admin | Delete a global slot |
| POST | `/{adId}/overrides` | Admin | Per-content override (replaces global slots at that position; always Fixed) |
| DELETE | `/overrides/{overrideId}` | Admin | Delete an override |
| GET | `/schedule-settings` | Admin | Current scheduling mode and parameters |
| DELETE | `/schedule-settings` | Admin | Drop the saved override and return to the `appsettings` defaults |
| PUT | `/schedule-settings` | Admin | Save mode and parameters (clamped by `Normalized()`) |
| POST | `/schedule-preview` | Admin | Sample schedule (+ ad pool) for a format (or all) + duration, using unsaved or saved options; stores nothing |

### Other modules

TV shows, seasons / episodes, subtitles, playback, view tracking (heartbeat), notifications, the AI assistant (`/api/ai/chat`) and the SignalR hub (`/hubs/notifications`) follow the same conventions. Run the API in Development and open Swagger for the full, current list.

---

## Security Hardening

| Area | What is in place |
|---|---|
| Refresh token | HttpOnly + Secure cookie, `Path=/api/auth`, rotated on every use, stored hashed, absent from response bodies |
| CSRF on refresh / logout | `Origin` must be in the CORS whitelist (the cookie is `SameSite=None` while FE and BE are on different domains) |
| Access token | Configurable (default 15 min, max 60), `ClockSkew = 0`, kept in memory by the frontend, revocable per user |
| Revocation | Per-user Redis mark checked on every request; triggered by ban, delete, role change, password change / reset |
| Passwords | BCrypt (work factor 12), shared `PasswordPolicy`, new password validated before the OTP is consumed |
| Email identity | Trimmed and lower-cased on register and lookup; unique index on `lower(email)` recommended (see [Roadmap](#roadmap)) |
| Self-protection | Admins cannot delete, ban or change the role of their own account; users cannot upgrade their own plan; admins cannot set `User.SubscriptionType` to a Premium value without a matching `UserSubscription` |
| OTP | 6 digits, 5 attempts / 15 min, constant-time compare, atomic 60 s resend cooldown, never logged |
| Plans | Admin-only management; the server is the source of truth for price, sale window and eligibility (the client's plan data is never trusted); free plans are always limited by `MaxPerUser` and guarded by an atomic lock against double clicks |
| Payments | HMAC-SHA512 signature over the raw query with constant-time compare, amount check, idempotent handler guarded by an atomic Redis lock, GMT+7 timestamps, `Backend:PublicUrl` from config (never from the `Host` header), client IP only from `ForwardedHeaders` |
| Payment redirect | `status` on the return URL is informational; the frontend must confirm through `/subscription-status` or `/history` |
| SePay IPN | No signature, so `X-Secret-Key` is verified before any lock / DB access, `order_status` / `transaction_status` are checked, the amount is compared with the order and the order's provider must be `sepay` |
| Revenue export | Admin only, audit-logged (who / range / status / rows), formula-injection guard on text cells, hard cap of 50,000 rows |
| Ad scheduling | Settings and preview are Admin-only; every value is clamped by `AdScheduleOptions.Normalized()`; the public schedule endpoint sends `Cache-Control: no-store` so a random plan is never shared between viewers |
| Rate limiting | `auth-strict` 10/min/IP · `auth-refresh` 60/min/IP · `ai` 20/min/user (5/min/IP for guests) · `playback` 30/min · `heartbeat` 20/min/user, 240/min/IP |
| Proxy headers | `X-Forwarded-*` is trusted only from private networks, so clients cannot spoof their IP |
| Response headers | `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, HSTS and a strict API CSP (non-Development). The UI's CSP is configured in the frontend's `vercel.json` |
| Request size | No global body-size limit; upload endpoints declare their own `[RequestSizeLimit]` |
| Email queue | Bounded (1000), at most 8 concurrent sends, 3 attempts each; dropped items are logged |

> Rate-limit counters are kept in memory per instance. Running N instances multiplies the effective limit by N.

---

## Installation & Setup

### Prerequisites

- .NET SDK matching the `TargetFramework` of the solution
- [PostgreSQL 15+](https://www.postgresql.org/)
- [Redis](https://redis.io/) or a free [Upstash](https://upstash.com/) database (TLS)
- [Cloudinary](https://cloudinary.com/) account
- [TMDB API key](https://www.themoviedb.org/settings/api)
- SMTP credentials (Gmail App Password works)
- Optional: Groq API key (AI assistant), VNPay sandbox and SePay accounts (payments)

### Steps

**1. Clone and restore**
```bash
git clone https://github.com/your-username/UIAMovie.git
cd UIAMovie
dotnet restore
```

**2. Configure secrets**

Copy `appsettings.json` to `appsettings.Development.json` (or use `dotnet user-secrets` / environment variables) and fill in the values from [Configuration](#configuration). Never commit real secrets.

**3. Apply migrations**
```bash
dotnet ef database update --project UIAMovie.Infrastructure --startup-project UIAMovie.API
```

**4. Run**
```bash
dotnet run --project UIAMovie.API
```

Swagger is available in Development at `/swagger`. A health message is served at `/`.

**5. Create the first plans**

`GET /api/payments/plans` is empty until a plan exists. Sign in as the seeded admin and create plans in the admin UI (**Plans** page) or with `POST /api/admin/plans` (see [Usage](#usage)).

### Payments & revenue wiring checklist

- NuGet: `dotnet add UIAMovie.API package ClosedXML` (Excel export).
- `Program.cs` must register `IRevenueService → RevenueService`, `ISubscriptionPlanService → SubscriptionPlanService`, `IPaymentOrderRepository`, `IPaymentReceiptNotifier`, the SePay / VNPay services, and the hosted service: `builder.Services.AddHostedService<PaymentExpiryWorker>();`.
- `MovieDbContext` must expose `DbSet<SubscriptionPlan>`, and the migration must be applied.
- After a pull that moves routes between controllers, check the console for `AmbiguousMatchException` (see [Payments notes](#notes)).

---

## Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=uiamovie;Username=postgres;Password=yourpassword"
  },
  "Jwt": {
    "SecretKey": "at-least-32-characters-random-secret",
    "Issuer": "UIAMovie",
    "Audience": "UIAMovieUsers",
    "AccessTokenMinutes": 15
  },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5173", "https://your-frontend.vercel.app" ]
  },
  "Backend": {
    "PublicUrl": "https://api.your-domain.com"
  },
  "Frontend": {
    "Url": "https://your-frontend.vercel.app"
  },
  "Redis": {
    "ConnectionString": "rediss://default:<password>@<host>:<port>"
  },
  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": "587",
    "SenderName": "UIAMovie",
    "SenderEmail": "your-email@gmail.com",
    "Username": "your-email@gmail.com",
    "Password": "your-app-password"
  },
  "Cloudinary": { "CloudName": "", "ApiKey": "", "ApiSecret": "" },
  "Tmdb": {
    "ApiKey": "",
    "BaseUrl": "https://api.themoviedb.org/3",
    "ImageBaseUrl": "https://image.tmdb.org/t/p/w500"
  },
  "Groq": { },
  "Sepay": { },
  "VNPay": {
    "TmnCode": "",
    "HashSecret": "",
    "BaseUrl": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"
  },
  "Ads": {
    "Schedule": {
      "Mode": "Fixed",
      "PreRollWeights": [5, 40, 40, 15],
      "MidRollIntervalRatio": 0.2,
      "JitterRatio": 0.25,
      "MinMidRollGapRatio": 0.1,
      "EndGuardRatio": 0.1,
      "MinDurationForMidRollSeconds": 0,
      "MaxMidRolls": 6,
      "PostRollChance": 0.5
    }
  }
}
```

`Groq` and `VNPay` bind to `GroqOptions` and `VnpayOptions`; see those classes for the exact keys. `Sepay` holds the SePay merchant credentials and the IPN secret used by `SepayPaymentService` (see its options class for the exact keys). Subscription plans are **not** configured here: they live in the database and are managed from the admin UI.

| Key | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Jwt:SecretKey` | HS256 signing key (≥ 32 chars) |
| `Jwt:Issuer` / `Jwt:Audience` | JWT claims validated on every request |
| `Jwt:AccessTokenMinutes` | Access token lifetime in minutes (default 15, capped at 60). Also drives `expiresIn` and the revocation-mark TTL |
| `Cors:AllowedOrigins` | Frontend origins. Also used to validate the `Origin` of `/auth/refresh-token` and `/auth/logout`. Environment variable form: `Cors__AllowedOrigins__0` |
| `Backend:PublicUrl` | Public HTTPS base URL of this API. **Required outside Development**; used to build the VNPay / SePay return URLs (never taken from the `Host` header). Env var: `Backend__PublicUrl` |
| `Frontend:Url` | Where the return endpoints redirect the browser. Falls back to the `FRONTEND_URL` environment variable; the `localhost:5173` fallback exists only in Development |
| `Redis:ConnectionString` | URL form `rediss://default:<password>@<host>:<port>` (parsed in `Program.cs`) |
| `VNPay:TmnCode` / `VNPay:HashSecret` | Merchant code and signing secret from the VNPay portal |
| `VNPay:BaseUrl` | Sandbox by default; switch to the production gateway URL when going live |
| `Sepay:*` | SePay merchant credentials and the IPN secret (`X-Secret-Key`) |
| `Email:*` | SMTP settings |
| `Cloudinary:*` | Cloudinary credentials |
| `Tmdb:ApiKey` | TMDB v3 key |
| `Ads:Schedule:Mode` | `Fixed` (default) or `Random`. Admins can override it at runtime from the admin UI |
| `Ads:Schedule:PreRollWeights` | Weight of "0, 1, 2, … opening ads" (max 6 entries). Default `[5,40,40,15]` favours more ads at the start |
| `Ads:Schedule:MidRollIntervalRatio` / `JitterRatio` | One mid-roll per this fraction of the duration (0.2 = every 20 %), ± jitter fraction |
| `Ads:Schedule:MinMidRollGapRatio` / `EndGuardRatio` | Minimum gap between mid-rolls and the guard zone at the end, both as a fraction of the video duration (0.1 = 10 %) |
| `Ads:Schedule:MinDurationForMidRollSeconds` | Optional floor in seconds below which a video gets no mid-roll; `0` (default) = every video is split by percentage |
| `Ads:Schedule:MaxMidRolls` / `PostRollChance` | Upper bound on mid-rolls (≤ 20) and probability (0–1) of a post-roll |

---

## Usage

### 1. Admin account

An admin account is seeded by the first migration (see the seed data in `MovieDbContext`). **Change its password immediately in any shared or production environment and never publish seeded credentials.**

### 2. Sync genres, then import content
```http
POST /api/genres/sync-tmdb
Authorization: Bearer <admin-access-token>

POST /api/movies/import-tmdb/550
Authorization: Bearer <admin-access-token>
```

### 3. Browse
```http
GET /api/movies/filter?GenreIds=<id>&MinRating=7&SortBy=rating&Page=1&PageSize=20
```

### 4. Login and refresh
```http
POST /api/auth/login
Content-Type: application/json

{ "email": "user@example.com", "password": "YourPassword1@" }
```
The response contains the `accessToken`; the refresh token arrives as a cookie. To renew the access token (the browser attaches the cookie itself):
```http
POST /api/auth/refresh-token
```
With `curl`/Postman, keep the cookie jar between calls and call over HTTPS.

### 5. Create Premium plans (admin)
```http
POST /api/admin/plans
Authorization: Bearer <admin-access-token>
Content-Type: application/json

{
  "planId": "monthly_premium",
  "name": "Premium Tháng",
  "description": "Xem không giới hạn, không quảng cáo",
  "priceVnd": 69000,
  "originalPriceVnd": null,
  "durationDays": 30,
  "isMostPopular": true,
  "isActive": true,
  "sortOrder": 1,
  "features": ["Full HD / 4K", "Không quảng cáo", "Tải phim offline"],
  "isTrial": false,
  "maxPerUser": null,
  "allowedEmailDomains": null,
  "startsAt": null,
  "endsAt": null
}
```
A free trial is the same call with `"priceVnd": 0` (for example `"planId": "trial_7d"`, `"durationDays": 7`). It becomes a trial automatically with `maxPerUser = 1`. A student plan sets `"allowedEmailDomains": "edu.vn"`. `startsAt` / `endsAt` are ISO timestamps (the admin UI converts from Vietnam time).

### 6. Buy Premium
```http
GET /api/payments/plans
Authorization: Bearer <access-token>      # optional, adds isEligible / ineligibleReason

POST /api/payments/create-order
Authorization: Bearer <access-token>
Content-Type: application/json

{ "planId": "monthly_premium", "paymentProvider": "vnpay" }
```
For `"vnpay"` redirect the browser to the returned `paymentUrl`; for `"sepay"` (`paymentMethod: "form_post"`) POST a form to `paymentUrl` with `formFields` in the returned order. For a free plan the response has `paymentMethod: "none"` and Premium is already active. After payment the user lands on `{Frontend}/payment/result?status=…&orderCode=…`; confirm with `GET /api/payments/subscription-status`.

### 7. Grant or revoke Premium manually (admin)
```http
POST /api/payments/admin/grant-subscription
Authorization: Bearer <admin-access-token>
Content-Type: application/json

{ "userId": "<guid>", "planId": "monthly_premium", "expiredAt": "2026-12-31T16:59:59Z" }

DELETE /api/payments/admin/subscription/<userId>
Authorization: Bearer <admin-access-token>
```

### 8. Configure ad scheduling
```http
GET /api/ads/schedule-settings
Authorization: Bearer <admin-access-token>

PUT /api/ads/schedule-settings
Authorization: Bearer <admin-access-token>
Content-Type: application/json

{ "mode": "Random", "preRollWeights": [5, 40, 40, 15], "midRollIntervalRatio": 0.2, "jitterRatio": 0.25,
  "minMidRollGapSeconds": 300, "endGuardSeconds": 120, "minDurationForMidRollSeconds": 480,
  "maxMidRolls": 6, "postRollChance": 0.5 }
```
Attach at least one active ad to a Global Slot (`POST /api/ads/{adId}/global-slots`) — in Random mode those slots form the ad pool. Then check a schedule:
```http
GET /api/ads/content/Movie/{movieId}?durationSeconds=7200
```
Each call returns a different plan; Fixed mode returns the same one. If `midRoll` is empty, check that `durationSeconds` is present and large enough.

---

## Deployment

### API on Render
1. Create a **Web Service** from this repository.
2. Build: `dotnet publish -c Release -o out` — Start: `dotnet out/UIAMovie.API.dll`
3. Add every setting above as an environment variable. Do not forget `Backend__PublicUrl` and `Frontend__Url` (or `FRONTEND_URL`).
4. Create a PostgreSQL database and use its connection string, then apply the migrations (including `AddSubscriptionPlans`) and create the first plans.
5. Render's proxy lives in a private network, which is why `ForwardedHeaders` trusts only `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` and `fc00::/7`. If you put Cloudflare in front, add its ranges.

### VNPay merchant portal
Register the IPN URL `https://<api-domain>/api/payments/vnpay/ipn` in the VNPay merchant portal. The IPN URL is configured there; it is **not** sent as a request parameter. The API must be reachable from the public internet for VNPay to call it.

### SePay portal
In my.sepay.vn → payment gateway → configuration, set the IPN URL to `https://<api-domain>/api/payments/sepay/ipn` and the secret that the API compares with the `X-Secret-Key` header. The return URLs are generated per order by the API (`/api/payments/sepay/return/{orderCode}/{success|error|cancel}`).

### Frontend on Vercel
- Set `VITE_API_URL=https://<api-domain>/api`.
- Add the Vercel domain to `Cors:AllowedOrigins`.
- When a same-domain `/api` rewrite is introduced, the refresh cookie can move from `SameSite=None` to `Lax`. Large uploads and SignalR should still go straight to the API.

### Upstash / Cloudinary
Create a Redis database and copy its TLS URL; copy Cloud Name, API Key and API Secret from the Cloudinary dashboard.

---

## Folder Structure

```
UIAMovie/
├── UIAMovie.API/                  # ASP.NET Core entry point
│   ├── Controllers/               # AuthController, PaymentController, AdminRevenueController,
│   │                              # AdminPlanController, MoviesController, ...
│   ├── Hubs/                      # SignalR (NotificationHub)
│   ├── Filter/                    # Validation error filter
│   ├── Middleware/                # Exception handling
│   ├── Services/                  # RevenueExcelExporter (ClosedXML), PaymentExpiryWorker
│   └── Program.cs                 # DI, auth, CORS, rate limiting, pipeline
│
├── UIAMovie.Application/          # Business logic
│   ├── AI/                        # Intent, orchestration, retrieval, tools
│   ├── Caching/                   # Cache key definitions (UserCacheKeys)
│   ├── DTOs/                      # incl. SubscriptionPlanDTO, AdminPlanDTO, UpsertPlanDTO
│   ├── Interfaces/                # Service and repository contracts
│   ├── Mappings/                  # UserMapper (shared User → UserDTO)
│   ├── Options/                   # AdScheduleOptions (Fixed / Random ad scheduling)
│   ├── Security/                  # PasswordPolicy, TokenRevocationService (policy, no libraries)
│   ├── Services/                  # AuthService, UserService, PaymentService, SubscriptionPlanService,
│   │                              # SubscriptionChecker, RevenueService (+ VnTime), VnpayPaymentService,
│   │                              # AdService, RandomAdPlanner, ...
│   └── Validators/                # FluentValidation rules
│
├── UIAMovie.Domain/               # Entities and constants (PaymentOrder, UserSubscription,
│                                  # SubscriptionPlan, Roles)
│
└── UIAMovie.Infrastructure/       # External integrations and data access
    ├── AI/                        # Groq provider, rate limiter
    ├── Caching/                   # RedisCacheService
    ├── Configuration/             # Cloudinary, TMDB, VNPay options, SessionCleanupWorker
    ├── Data/                      # DbContext, repositories, migrations
    ├── Email/                     # EmailService, EmailQueue + worker, payment receipts
    ├── Messaging/                 # Realtime notification sender
    └── Security/                  # JwtTokenGenerator, TwoFactorAuthProvider (library-specific mechanisms)
```

---

## Git Workflow

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/): `type(scope): short summary`, with a body for the details when needed.

| Type | When to use |
|---|---|
| `feat` | A new feature (`feat(plans): admin-configurable subscription plans`) |
| `fix` | A bug fix (`fix(payment): honour late callbacks for cancelled orders`) |
| `docs` | README / comments only |
| `refactor` | Code change without behaviour change |
| `perf` | Performance improvement |
| `test` | Adding or fixing tests |
| `chore` | Tooling, dependencies, config, migrations housekeeping |

```bash
git checkout -b feat/subscription-plans     # work on a branch
git status
git add <files>                             # or: git add -A
git commit -m "feat(plans): admin-configurable plans with free trials" \
  -m "- SubscriptionPlan entity, service and /api/admin/plans
- eligibility rules in create-order and GET /payments/plans
- free plans activate without a gateway"
git push -u origin feat/subscription-plans  # then open a pull request into main
```

Schema changes go in their own commit together with the generated migration (`dotnet ef migrations add <Name>`), so a deploy can apply them in one step. Never commit real secrets.

---

## Roadmap

**Payments & subscriptions**
- [ ] Unique index on `SubscriptionPlans.PlanId` as a last line of defence behind the check in `AdminCreateAsync`
- [ ] Store `PlanId` directly on `UserSubscription` (today it is derived from `LastPaymentOrderId`, which a manual grant clears)
- [ ] Decide on a refund / cancel-at-period-end policy (today cancelling forfeits the remaining time)
- [ ] Concurrency token (`xmin` on PostgreSQL) on `PaymentOrder` as a second line of defence behind the Redis lock
- [ ] Count active subscribers with `COUNT` instead of loading the rows in `GetSummaryAsync`
- [ ] Rate-limit (or queue) the Excel export and stream large files; inject `TimeProvider` into `RevenueService` for tests
- [ ] Unit tests for `CheckEligibility` (limit reached, trial with active Premium, e-mail domain, sale window), `VnTime` boundaries (23:59 / 00:00 VN), `successRate`, month-over-month `null`, and the SePay / VNPay confirmation paths
- [ ] Unified response shape for `PaymentController`, `AdminPlanController` and `AdminRevenueController` (they still return bare objects / `{ message }`)
- [ ] Plan usage statistics (how many users bought each plan)

**Security & accounts**
- [ ] Unique index on `lower(email)` (`CREATE UNIQUE INDEX "IX_Users_LowerEmail" ON "Users" (lower("Email"));`) after checking for case-insensitive duplicates
- [ ] `IPasswordHasher` abstraction so the Application layer no longer references BCrypt directly
- [ ] Update `LastLogin` with `ExecuteUpdate` instead of saving the whole user row
- [ ] Refresh token **reuse detection** (revoke every session of a user when an already-rotated token is replayed outside the grace window)
- [ ] Same-domain `/api` proxy, then `SameSite=Lax` for the refresh cookie
- [ ] Distributed rate limiting and TOTP replay protection (Redis) for multi-instance deployments
- [ ] Content-Security-Policy for the React app in `vercel.json` (start with `Report-Only`)

**Ads**
- [ ] Persist the schedule settings in a database table instead of the cache (survives cache eviction / restarts)
- [ ] Unit tests for `RandomAdPlanner` (fixed-seed `Random`): pre-roll cap by pool size, mid-roll bounds and spacing, no adjacent repeats, `durationSeconds = 0`
- [ ] Endpoint to list a content's (or show's) Overrides (the admin UI can only list Global Slots)
- [ ] Frequency capping per user / per ad and basic impression / skip statistics

**Platform & product**
- [ ] **Protected video delivery** — Cloudinary `authenticated` type + signed HLS URLs, `DeliveryType` column, re-upload of existing videos (current signed links do not expire on the Free plan)
- [ ] Unified response envelope and camelCase keys across all controllers
- [ ] Recommendation engine based on watch history and ratings
- [ ] Admin analytics dashboard
- [ ] Docker / docker-compose for local development
- [ ] CI/CD with GitHub Actions (build, test, deploy to Render)

---

## License

This project is licensed under the [MIT License](LICENSE).

---

> Built as a graduation project — UIAMovie, 2025.