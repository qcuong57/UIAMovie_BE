# UIAMovie — Movie & TV Streaming Platform (REST API)

A RESTful API backend for a Netflix-style streaming platform, built with ASP.NET Core. It covers account security (email OTP, 2FA, rotating refresh tokens in HttpOnly cookies), movie and TV catalogue management with TMDB import, Cloudinary video hosting, Premium subscriptions paid through VNPay or SePay (with an admin revenue dashboard and Excel export), real-time notifications over SignalR, an AI movie assistant, and Redis caching.

The React frontend (Vite, deployed on Vercel) lives in a separate repository and talks to this API over HTTPS.

---

## Table of Contents

- [Features](#features)
- [Tech Stack](#tech-stack)
- [Architecture Overview](#architecture-overview)
- [Authentication Flow](#authentication-flow)
- [Token Revocation](#token-revocation)
- [Payments (VNPay & SePay)](#payments-vnpay--sepay)
- [API Endpoints](#api-endpoints)
- [Security Hardening](#security-hardening)
- [Installation & Setup](#installation--setup)
- [Configuration](#configuration)
- [Usage](#usage)
- [Deployment](#deployment)
- [Folder Structure](#folder-structure)
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
- Premium subscription paid through **VNPay** (redirect) or **SePay** (form post) — monthly 69,000 ₫ / yearly 599,000 ₫. Callbacks are verified (VNPay: HMAC-SHA512 signature; SePay: secret header + order cross-check), idempotent and safe against the browser-return / IPN race (see [Payments (VNPay & SePay)](#payments-vnpay--sepay))
- Abandoned `pending` orders are moved to `expired` by `PaymentExpiryWorker` (data is never deleted)
- Users can view their payment history and subscription status, and cancel their own Premium (access ends **immediately**; the unused time is **not refunded**)
- Buying again while Premium is active is allowed and **extends** the current expiry (renewal); the API does not block `create-order` for active subscribers
- Plans advertise: Full HD / 4K, no ads, offline download, plus priority support on the yearly plan. There is no trial period and no refund policy
- `Premium` can only be set through payment or `admin/grant-subscription`, so every Premium user has a `UserSubscription` row (what `SubscriptionChecker` reads)
- Admin tools to grant or revoke subscriptions, plus a revenue dashboard (summary, chart, by-plan, order list) whose dates are computed in **Vietnam time (GMT+7)** and filtered in the database, and an **Excel export** (3 sheets) with an audit log line
- Watch history with progress, heartbeat-based view tracking and view statistics
- Advertisements module

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
│  Payment, Notification, AI, ...)                          │
│        │                       │                          │
│  Redis cache (Upstash)   Generic / specialised repos      │
│                          (EF Core + PostgreSQL)           │
└────────┬──────────────┬───────────────┬──────────────────┘
         │              │               │
   Cloudinary        TMDB API       VNPay / Groq / SMTP
```

**Layer responsibilities**

The solution follows Clean Architecture: dependencies point inward (API → Application → Domain; Infrastructure implements Application's interfaces).

- **Controllers / Hubs** — HTTP routing, request and response shaping, authorization and rate-limit attributes
- **Services** — business logic, cache management, orchestration
- **Repositories** — data access via EF Core (`Repository<T>` plus specialised repositories such as `IUserRepository`)
- **Middleware** — security headers, global exception handling
- **Infrastructure** — Redis, Cloudinary, JWT generator, email queue, TMDB / Groq / VNPay clients

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

## Payments (VNPay & SePay)

Both gateways share one order model and one confirmation core (`ConfirmOrderAsync`). The gateway is chosen by `paymentProvider` (`"vnpay"` | `"sepay"`) in `create-order`.

### Flow

```
1. POST /api/payments/create-order { planId, paymentProvider }
      → PaymentOrder (status=pending, expires in 15 min)         the user's other still-valid pending orders become `expired` ("Thay bằng đơn mới")
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
        → reject if no longer pending                              idempotent
        → check amount (VND)
        → ONE SaveChanges: order + UserSubscription + User.SubscriptionType
        → invalidate user / subscription caches

4. The return endpoints redirect to {Frontend}/payment/result?status=…&orderCode=…
```

`status` in that URL is for display only. The frontend confirms the real result through `GET /api/payments/history` (looks the order up by `orderCode`) or `GET /api/payments/subscription-status`.

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

`pending` → `success` | `failed` | `expired`.

- Listings and statistics show a `pending` order past `ExpiredAt` as **`expired`** (a display rule only — the row is not changed, so a late callback is still processed).
- `PaymentExpiryWorker` runs every 10 minutes and moves `pending` orders older than `ExpiredAt + 30 min` (grace) to `expired` with a conditional `UPDATE`, which is safe when several instances run it at once. It never deletes data. A callback that confirms an order in the same moment wins, because the `UPDATE` only touches rows that are still `pending`.
- `failed` means the gateway reported an error or the amount did not match.
- **One open order per user:** `create-order` marks the user's other still-valid `pending` orders as `expired` (reason "Thay bằng đơn mới") in the same `SaveChanges` that inserts the new order, so opening and abandoning the gateway never piles up `pending` rows.
- **Late payments are honoured:** a *successful* callback for an `expired` order (worker timeout, superseded order, slow bank transfer) still confirms the order and grants Premium and clears the failure reason; a *failed* callback for an expired order is ignored. Money that left the customer's account is never left without a plan.
- **Order code:** `ORD-yyyyMMdd-XXXXXXXX` (8 random hex characters from a cryptographic RNG), checked for existence before use; `PaymentOrder.OrderCode` also has a unique index as the last line of defence.

### Database indexes & migration

`MovieDbContext` defines, for payments: unique `PaymentOrder.OrderCode`; `PaymentOrder (UserId, Status)`, `(Status, ExpiredAt)`, `PaidAt`, `CreatedAt` (expiry worker, "one open order per user", revenue queries); and a **unique** `UserSubscription.UserId`.

Before applying the migration that makes `UserSubscription.UserId` unique, make sure no user has two rows (keep the one with the latest `ExpiredAt`):

```sql
SELECT "UserId", COUNT(*) FROM "UserSubscriptions" GROUP BY "UserId" HAVING COUNT(*) > 1;
```

```bash
dotnet ef migrations add PaymentIndexes --project UIAMovie.Infrastructure --startup-project UIAMovie.API
dotnet ef database update --project UIAMovie.Infrastructure --startup-project UIAMovie.API
```

### Notes

- **Stacking:** paying while Premium is still active extends from the current expiry date, not from today. The frontend therefore offers "renew" instead of blocking the purchase.
- **Source of truth:** use `GET /payments/subscription-status` (backed by `UserSubscription` and `ExpiredAt`) to decide who is Premium. `User.SubscriptionType` is a convenience copy and is not read by `SubscriptionChecker`.
- **No `planId` on the subscription:** `subscription-status` returns `subscriptionType: "Free" | "Premium"` only, so a client cannot tell monthly from yearly (see [Roadmap](#roadmap)).
- **Cancel:** `DELETE /payments/subscription/me` deletes the subscription (no remaining-time refund, no grace period) and clears the Premium cache.
- **Atomicity:** all repositories share one scoped `DbContext`, so a single `SaveChanges` stores the order, the subscription and the user together. If you ever stop sharing the context, revisit this.
- **Limit of the lock:** the Redis lock only protects within one Redis instance. For absolute safety against double-crediting, add a concurrency token (`xmin` on PostgreSQL) to `PaymentOrder` (see [Roadmap](#roadmap)).
- Production must set `Backend:PublicUrl` and `Frontend:Url` (or `FRONTEND_URL`); the API refuses to guess them outside Development.
- **Duplicate routes break everything:** the admin revenue / order endpoints live only in `AdminRevenueController` (URLs unchanged: `/api/payments/admin/...`). Declaring the same route again in `PaymentController` makes ASP.NET Core throw `AmbiguousMatchException` and every one of those requests returns HTTP 500 before reaching any action.

### Revenue reporting & Excel export

Implemented by `RevenueService` (Application) and `AdminRevenueController` + `RevenueExcelExporter` (API).

- **Time zone:** the database stores UTC; every report day / month is **Vietnam time (GMT+7, no DST)**. `VnTime` converts the VN day boundary to UTC and the database does the filtering, so no table is loaded into memory. `from` / `to` are *dates*, both inclusive, and `from` must not be after `to` (otherwise `400`). API timestamps are UTC.
- **Revenue** is the sum of `success` orders by **paid date**, as the customer paid through the gateway — **before** gateway fees and refunds / voids.
- **Summary** (`GET /admin/revenue/summary`): `totalRevenue`, `totalOrders`, `successOrders`, `pendingOrders` (not yet expired), `failedOrders` (gateway error / wrong amount only), `expiredOrders`, `successRate = success / (success + failed + expired)` (pending excluded), `averageOrderValue`, `todayRevenue` / `todayOrders`, `currentMonthRevenue` / `currentMonthOrders`, `lastMonthRevenue`, `monthOverMonthGrowth` (**`null`** when last month is 0 — no fake 100 %), `activePremiumUsers`, `expiringIn7Days`, `generatedAt`.
- **Chart** (`GET /admin/revenue/chart`): `groupBy=month&year=…` or `groupBy=day&year=…&month=…`; every day / month is returned (0 when empty) so the chart never has gaps. `year` defaults to the current VN year.
- **Orders** (`GET /admin/orders`): filter (`status`, `planId`, `provider`, `from`, `to` by **created date**, `search` by order code or e-mail) and paging run in the database; `pageSize` is clamped to 1–100. `status` is the effective status (a stale `pending` is `expired`).
- **Export** (`GET /admin/revenue/export?from&to&status&provider&planId`): returns `doanh-thu_<yyyyMMdd>_<yyyyMMdd>.xlsx` (or `doanh-thu_toan-thoi-gian.xlsx`) with three sheets — *Tổng quan*, *Theo gói*, *Đơn hàng*. Without `status` only `success` orders are exported; successful orders are filtered by **paid date**, other statuses by **created date**. More than 50,000 rows → `400` asking to narrow the range. Cells that start with `= + - @` are neutralised against spreadsheet formula injection, and each export writes an audit log line (admin id, range, status, row count) because the file contains customer e-mails.

---

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
| GET | `/plans` | Public | Subscription plans |
| POST | `/create-order` | User | Create an order for `vnpay` (redirect) or `sepay` (form post: `paymentUrl` + `formFields`) |
| GET | `/vnpay/ipn` | VNPay | Server-to-server callback (VNPay uses GET) |
| GET | `/vnpay/return` | Public | Browser return; processes the callback idempotently, then redirects to the frontend |
| POST | `/sepay/ipn` | SePay | Server-to-server callback, authenticated by the `X-Secret-Key` header |
| GET | `/sepay/return/{orderCode}/{result}` | Public | Browser return (`success` / `error` / `cancel`), then redirects to the frontend |
| GET | `/history` | User | Own payment history |
| GET | `/subscription-status` | User | Current subscription (`isPremium`, `daysRemaining`, `isExpiringSoon`) |
| DELETE | `/subscription/me` | User | Cancel own Premium immediately (no refund) |
| POST | `/admin/grant-subscription` | Admin | Grant / extend Premium for any user |
| DELETE | `/admin/subscription/{userId}` | Admin | Revoke Premium |
| GET | `/admin/revenue/summary` | Admin | Totals, today / this month / last month, growth, success rate, active Premium users (Vietnam time) |
| GET | `/admin/revenue/chart` | Admin | `groupBy=month&year=…` or `groupBy=day&year=…&month=…` (`year` 2000–2100, `month` 1–12) |
| GET | `/admin/revenue/by-plan` | Admin | Revenue split per plan, optional `from` / `to` (VN dates, inclusive) |
| GET | `/admin/orders` | Admin | Orders with filter (`status`, `planId`, `provider`, `from`, `to`, `search`), paging in the database |
| GET | `/admin/revenue/export` | Admin | `.xlsx` report (3 sheets); defaults to successful orders; audit-logged |

> The five `/admin/revenue/*` and `/admin/orders` routes are served by `AdminRevenueController`; `grant-subscription` and `subscription/{userId}` stay in `PaymentController`.

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

### Other modules

TV shows, seasons / episodes, subtitles, playback, view tracking (heartbeat), ads, notifications, the AI assistant (`/api/ai/chat`) and the SignalR hub (`/hubs/notifications`) follow the same conventions. Run the API in Development and open Swagger for the full, current list.

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
| Payments | HMAC-SHA512 signature over the raw query with constant-time compare, amount check, idempotent handler guarded by an atomic Redis lock, GMT+7 timestamps, `Backend:PublicUrl` from config (never from the `Host` header), client IP only from `ForwardedHeaders` |
| Payment redirect | `status` on the return URL is informational; the frontend must confirm through `/subscription-status` or `/history` |
| SePay IPN | No signature, so `X-Secret-Key` is verified before any lock / DB access, `order_status` / `transaction_status` are checked, the amount is compared with the order and the order's provider must be `sepay` |
| Revenue export | Admin only, audit-logged (who / range / status / rows), formula-injection guard on text cells, hard cap of 50,000 rows |
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
- Optional: Groq API key (AI assistant), VNPay sandbox account (payments)

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

### Payments & revenue wiring checklist

- NuGet: `dotnet add UIAMovie.API package ClosedXML` (Excel export).
- `Program.cs` must register `IRevenueService → RevenueService`, `IPaymentOrderRepository`, the SePay / VNPay services, and the hosted service: `builder.Services.AddHostedService<PaymentExpiryWorker>();`.
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
  }
}
```

`Groq` and `VNPay` bind to `GroqOptions` and `VnpayOptions`; see those classes for the exact keys. `Sepay` holds the SePay merchant credentials and the IPN secret used by `SepayPaymentService` (see its options class for the exact keys).

| Key | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Jwt:SecretKey` | HS256 signing key (≥ 32 chars) |
| `Jwt:Issuer` / `Jwt:Audience` | JWT claims validated on every request |
| `Jwt:AccessTokenMinutes` | Access token lifetime in minutes (default 15, capped at 60). Also drives `expiresIn` and the revocation-mark TTL |
| `Cors:AllowedOrigins` | Frontend origins. Also used to validate the `Origin` of `/auth/refresh-token` and `/auth/logout`. Environment variable form: `Cors__AllowedOrigins__0` |
| `Backend:PublicUrl` | Public HTTPS base URL of this API. **Required outside Development**; used to build the VNPay return URL (never taken from the `Host` header). Env var: `Backend__PublicUrl` |
| `Frontend:Url` | Where `/vnpay/return` redirects the browser. Falls back to the `FRONTEND_URL` environment variable; the `localhost:5173` fallback exists only in Development |
| `Redis:ConnectionString` | URL form `rediss://default:<password>@<host>:<port>` (parsed in `Program.cs`) |
| `VNPay:TmnCode` / `VNPay:HashSecret` | Merchant code and signing secret from the VNPay portal |
| `VNPay:BaseUrl` | Sandbox by default; switch to the production gateway URL when going live |
| `Email:*` | SMTP settings |
| `Cloudinary:*` | Cloudinary credentials |
| `Tmdb:ApiKey` | TMDB v3 key |

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

### 5. Buy Premium
```http
POST /api/payments/create-order
Authorization: Bearer <access-token>
Content-Type: application/json

{ "planId": "monthly_premium", "paymentProvider": "vnpay" }
```
For `"vnpay"` redirect the browser to the returned `paymentUrl`; for `"sepay"` (`paymentMethod: "form_post"`) POST a form to `paymentUrl` with `formFields` in the returned order. After payment the user lands on `{Frontend}/payment/result?status=…&orderCode=…`; confirm with `GET /api/payments/subscription-status`.

---

## Deployment

### API on Render
1. Create a **Web Service** from this repository.
2. Build: `dotnet publish -c Release -o out` — Start: `dotnet out/UIAMovie.API.dll`
3. Add every setting above as an environment variable. Do not forget `Backend__PublicUrl` and `Frontend__Url` (or `FRONTEND_URL`).
4. Create a PostgreSQL database and use its connection string.
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
│   ├── Controllers/               # AuthController, PaymentController, AdminRevenueController, MoviesController, ...
│   ├── Hubs/                      # SignalR (NotificationHub)
│   ├── Filter/                    # Validation error filter
│   ├── Middleware/                # Exception handling
│   ├── Services/                  # RevenueExcelExporter (ClosedXML), PaymentExpiryWorker
│   └── Program.cs                 # DI, auth, CORS, rate limiting, pipeline
│
├── UIAMovie.Application/          # Business logic
│   ├── AI/                        # Intent, orchestration, retrieval, tools
│   ├── Caching/                   # Cache key definitions (UserCacheKeys)
│   ├── DTOs/
│   ├── Interfaces/                # Service and repository contracts
│   ├── Mappings/                  # UserMapper (shared User → UserDTO)
│   ├── Security/                  # PasswordPolicy, TokenRevocationService (policy, no libraries)
│   ├── Services/                  # AuthService, UserService, PaymentService, RevenueService (+ VnTime), VnpayPaymentService, SubscriptionChecker, ...
│   └── Validators/                # FluentValidation rules
│
├── UIAMovie.Domain/               # Entities and constants (PaymentOrder, UserSubscription, Roles)
│
└── UIAMovie.Infrastructure/       # External integrations and data access
    ├── AI/                        # Groq provider, rate limiter
    ├── Caching/                   # RedisCacheService
    ├── Configuration/             # Cloudinary, TMDB, VNPay options, SessionCleanupWorker
    ├── Data/                      # DbContext, repositories, migrations
    ├── Email/                     # EmailService, EmailQueue + worker
    ├── Messaging/                 # Realtime notification sender
    └── Security/                  # JwtTokenGenerator, TwoFactorAuthProvider (library-specific mechanisms)
```

---

## Roadmap

**Payments & subscriptions**
- [ ] Store `PlanId` on `UserSubscription` and return it from `subscription-status`, so the UI can mark the current plan (monthly vs yearly)
- [ ] Decide on a refund / cancel-at-period-end policy (today cancelling forfeits the remaining time)
- [ ] Concurrency token (`xmin` on PostgreSQL) on `PaymentOrder` as a second line of defence behind the Redis lock
- [ ] Count active subscribers with `COUNT` instead of loading the rows in `GetSummaryAsync`; validate the `status` value of the export (unknown values silently return an empty file)
- [ ] Rate-limit (or queue) the Excel export and stream large files; inject `TimeProvider` into `RevenueService` for tests
- [ ] Unit tests for `VnTime` boundaries (23:59 / 00:00 VN), `successRate`, month-over-month `null`, and the SePay / VNPay confirmation paths
- [ ] Unified response shape for `PaymentController` and `AdminRevenueController` (they still return bare `{ message }` objects)

**Security & accounts**
- [ ] Unique index on `lower(email)` (`CREATE UNIQUE INDEX "IX_Users_LowerEmail" ON "Users" (lower("Email"));`) after checking for case-insensitive duplicates
- [ ] `IPasswordHasher` abstraction so the Application layer no longer references BCrypt directly
- [ ] Update `LastLogin` with `ExecuteUpdate` instead of saving the whole user row
- [ ] Refresh token **reuse detection** (revoke every session of a user when an already-rotated token is replayed outside the grace window)
- [ ] Same-domain `/api` proxy, then `SameSite=Lax` for the refresh cookie
- [ ] Distributed rate limiting and TOTP replay protection (Redis) for multi-instance deployments
- [ ] Content-Security-Policy for the React app in `vercel.json` (start with `Report-Only`)

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