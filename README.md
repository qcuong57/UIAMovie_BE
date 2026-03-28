# UIAMovie — Movie Streaming REST API

A production-ready RESTful API backend for a movie streaming platform, built with ASP.NET Core 8. It handles user authentication with 2FA, movie management with TMDB integration, streaming via Cloudinary, and real-time caching with Redis.

---

## Description

UIAMovie is the backend service powering a Netflix-style movie streaming application. It provides a comprehensive API for browsing movies, managing user accounts, writing reviews, and streaming video content. The system integrates with The Movie Database (TMDB) to import rich movie metadata, uses Cloudinary for video hosting, and Redis for high-performance caching.

---

## Features

### Authentication & Security
- JWT-based authentication with access + refresh token pair
- Refresh token rotation with sliding 7-day expiry window
- Two-factor authentication (2FA) via email OTP
- Enable / disable 2FA with OTP verification
- Forgot password and reset password via email OTP
- Role-based access control (`Admin` / `User`)

### Movie Management
- Full CRUD for movies (Admin only)
- TMDB integration — import movies, cast, directors, trailers, images in one call
- Genre sync from TMDB
- Video upload and streaming via Cloudinary
- Search by title and by actor name
- Advanced filtering by genre, rating, release date, country, with sorting and pagination
- Trending movies endpoint

### User Management
- User profile management (avatar, username, subscription type)
- Admin dashboard — list, filter, update role, delete users
- Watch history tracking with progress
- Favorites list
- Change password

### Reviews & Ratings
- Create, update, delete reviews (1–10 star rating)
- Per-movie rating statistics with distribution breakdown
- Pagination on movie reviews
- Admin moderation — remove any review

### Performance
- Redis caching (via Upstash) with pipeline batch invalidation
- `GetOrSetAsync` pattern to eliminate redundant DB round-trips
- `FindOneAsync` / `FindAsync` with EF Core expressions — no in-memory scanning
- `AsNoTracking()` on all read queries

---

## Tech Stack

| Category | Technology |
|---|---|
| Runtime | .NET 8 / ASP.NET Core |
| ORM | Entity Framework Core 8 |
| Database | PostgreSQL |
| Cache | Redis (Upstash) via StackExchange.Redis |
| Authentication | JWT Bearer + BCrypt |
| Email | MailKit / SMTP |
| Video Storage | Cloudinary |
| TMDB Integration | TMDB REST API v3 |
| Validation | FluentValidation |
| 2FA | OtpNet (TOTP) |
| Serialization | Newtonsoft.Json |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│                    Client (Mobile / Web)             │
└───────────────────────┬─────────────────────────────┘
                        │ HTTPS
┌───────────────────────▼─────────────────────────────┐
│               ASP.NET Core 8 API                     │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────┐  │
│  │ Controllers  │  │  Middleware  │  │ Validators│  │
│  └──────┬───────┘  └──────────────┘  └───────────┘  │
│         │                                            │
│  ┌──────▼───────────────────────────────────────┐   │
│  │            Application Services               │   │
│  │  AuthService │ MovieService │ UserService     │   │
│  │  GenreService │ RatingReviewService │ ...     │   │
│  └──────┬────────────────────────┬──────────────┘   │
│         │                        │                   │
│  ┌──────▼──────┐         ┌───────▼──────────────┐   │
│  │ Redis Cache │         │  Generic Repository   │   │
│  │  (Upstash)  │         │  (EF Core + PgSQL)   │   │
│  └─────────────┘         └──────────────────────┘   │
└─────────────────────────────────────────────────────┘
         │                          │
┌────────▼──────────┐   ┌──────────▼──────────────────┐
│   Cloudinary      │   │   TMDB API                   │
│  (Video Storage)  │   │  (Movie Metadata)            │
└───────────────────┘   └─────────────────────────────┘
```

**Layer responsibilities:**

- **Controllers** — HTTP routing, request/response shaping, authorization attributes
- **Services** — Business logic, cache management, orchestration
- **Repositories** — Data access via EF Core; `Repository<T>` (generic) and `MovieRepository` (specialized with eager loading)
- **Middleware** — Global exception handling, JWT pipeline
- **Infrastructure** — Redis, Cloudinary, JWT generator, email service, TMDB HTTP client

---

## API Endpoints

### Auth — `/api/auth`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| POST | `/register` | Public | Register a new account |
| POST | `/login` | Public | Login; returns tokens or OTP prompt |
| POST | `/otp/send` | Public | Resend OTP |
| POST | `/otp/verify` | Public | Verify OTP → receive tokens |
| POST | `/2fa/enable` | User | Send OTP to enable 2FA |
| POST | `/2fa/disable` | User | Verify OTP then disable 2FA |
| POST | `/forgot-password` | Public | Send reset OTP to email |
| POST | `/reset-password` | Public | Reset password with OTP |
| POST | `/refresh-token` | Public | Rotate refresh token |
| POST | `/logout` | User | Revoke all sessions |

### Movies — `/api/movies`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/` | Public | List all movies (paginated) |
| GET | `/{id}` | Public | Movie detail with cast, genres, videos |
| GET | `/trending` | Public | Trending movies |
| GET | `/filter` | Public | Filter by genre, rating, country, date |
| GET | `/search` | Public | Search by title |
| GET | `/search/actor` | Public | Search by actor name |
| POST | `/` | Admin | Create movie |
| POST | `/import-tmdb/{tmdbId}` | Admin | Import movie from TMDB |
| POST | `/{id}/upload-video` | Admin | Upload video to Cloudinary |
| PUT | `/{id}` | Admin | Update movie |
| DELETE | `/{id}` | Admin | Delete movie |

### Users — `/api/user`

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/me` | User | Get own profile |
| PUT | `/me` | User | Update own profile |
| POST | `/me/change-password` | User | Change password |
| GET | `/` | Admin | List all users (search, filter, paginate) |
| GET | `/{id}` | Admin | Get user by ID |
| PUT | `/{id}` | Admin | Update any user |
| PATCH | `/{id}/role` | Admin | Change user role |
| DELETE | `/{id}` | Admin | Delete user |

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
| GET | `/check/{movieId}` | User | Check if reviewed |
| DELETE | `/admin/{reviewId}` | Admin | Remove any review |

---

## Installation & Setup

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL 15+](https://www.postgresql.org/)
- [Redis](https://redis.io/) or a free [Upstash](https://upstash.com/) instance
- [Cloudinary](https://cloudinary.com/) account
- [TMDB API key](https://www.themoviedb.org/settings/api)
- SMTP credentials (Gmail App Password recommended)

### Steps

**1. Clone the repository**
```bash
git clone https://github.com/your-username/UIAMovie.git
cd UIAMovie
```

**2. Restore dependencies**
```bash
dotnet restore
```

**3. Configure environment variables**

Copy the example file and fill in your values:
```bash
cp UIAMovie.API/appsettings.json UIAMovie.API/appsettings.Development.json
```
Then edit `appsettings.Development.json` (see [Environment Variables](#environment-variables)).

**4. Apply database migrations**
```bash
dotnet ef database update --project UIAMovie.Infrastructure --startup-project UIAMovie.API
```

**5. Run the API**
```bash
dotnet run --project UIAMovie.API
```

The API will be available at `https://localhost:5001` and Swagger UI at `https://localhost:5001/swagger`.

---

## Environment Variables

Configure these in `appsettings.json` or as environment variables:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=uiamovie;Username=postgres;Password=yourpassword"
  },
  "Jwt": {
    "SecretKey": "your-256-bit-secret-key-minimum-32-characters",
    "Issuer": "UIAMovie",
    "Audience": "UIAMovieUsers"
  },
  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": "587",
    "SenderName": "UIAMovie",
    "SenderEmail": "your-email@gmail.com",
    "Username": "your-email@gmail.com",
    "Password": "your-app-password"
  },
  "Redis": {
    "ConnectionString": "your-upstash-redis-url:port,password=your-password,ssl=True"
  },
  "Cloudinary": {
    "CloudName": "your-cloud-name",
    "ApiKey": "your-api-key",
    "ApiSecret": "your-api-secret"
  },
  "Tmdb": {
    "ApiKey": "your-tmdb-api-key",
    "BaseUrl": "https://api.themoviedb.org/3",
    "ImageBaseUrl": "https://image.tmdb.org/t/p/w500"
  }
}
```

| Variable | Description |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string |
| `Jwt:SecretKey` | HS256 signing key (min 32 chars) |
| `Jwt:Issuer` | JWT issuer claim |
| `Jwt:Audience` | JWT audience claim |
| `Email:SmtpHost` | SMTP server hostname |
| `Email:Password` | SMTP password or app password |
| `Redis:ConnectionString` | Redis / Upstash connection string |
| `Cloudinary:CloudName` | Cloudinary cloud name |
| `Cloudinary:ApiKey` | Cloudinary API key |
| `Cloudinary:ApiSecret` | Cloudinary API secret |
| `Tmdb:ApiKey` | TMDB v3 API key |

---

## Usage

### 1. Seed the default admin account

The admin account is seeded automatically on first migration:
- **Email:** `quoccuong572003@gmail.com`
- **Password:** `Quoccuong572003@`

> Change these credentials immediately after first login in production.

### 2. Sync genres from TMDB (do this first)

```http
POST /api/genres/sync-tmdb
Authorization: Bearer <admin-token>
```

### 3. Import a movie from TMDB

```http
POST /api/movies/import-tmdb/550
Authorization: Bearer <admin-token>
```

### 4. Browse movies

```http
GET /api/movies/filter?GenreIds=<id>&MinRating=7&SortBy=rating&Page=1&PageSize=20
```

### 5. Login and refresh tokens

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "YourPassword1@"
}
```

On token expiry, rotate with:
```http
POST /api/auth/refresh-token
Content-Type: application/json

{
  "refreshToken": "<your-refresh-token>"
}
```

---

## Deployment

### Render (recommended for API + PostgreSQL)

1. Create a new **Web Service** pointing to this repository.
2. Set **Build Command:** `dotnet publish -c Release -o out`
3. Set **Start Command:** `dotnet out/UIAMovie.API.dll`
4. Add all environment variables in the Render dashboard.
5. Create a **PostgreSQL** database on Render and copy the connection string.

### Upstash (Redis)

1. Create a free Redis database at [upstash.com](https://upstash.com/).
2. Copy the **REST URL with password** formatted as a StackExchange.Redis connection string.

### Cloudinary

1. Sign up at [cloudinary.com](https://cloudinary.com/).
2. Copy Cloud Name, API Key, and API Secret from the dashboard.

---

## Folder Structure

```
UIAMovie/
├── UIAMovie.API/                  # ASP.NET Core entry point
│   ├── Controllers/               # HTTP controllers
│   ├── Middleware/                # Exception handling, JWT middleware
│   └── appsettings.json          # Configuration
│
├── UIAMovie.Application/          # Business logic layer
│   ├── DTOs/                     # Request and response models
│   ├── Interfaces/               # Service and infrastructure contracts
│   ├── Services/                 # AuthService, MovieService, UserService, etc.
│   └── Validators/               # FluentValidation rules
│
├── UIAMovie.Domain/               # Domain entities and constants
│   ├── Constants/                # Role constants
│   └── Entities/                 # User, Movie, Genre, Person, RatingReview, etc.
│
└── UIAMovie.Infrastructure/       # External integrations and data access
    ├── Caching/                  # RedisCacheService
    ├── Configuration/            # CloudinaryService, TmdbConfiguration
    ├── Data/
    │   ├── MovieDbContext.cs     # EF Core DbContext + model config + seed
    │   └── Repositories/        # GenericRepository, MovieRepository
    └── Security/                 # JwtTokenGenerator, TwoFactorAuthProvider
```

---

## Screenshots

> *Swagger UI and frontend screenshots — coming soon.*

To view the interactive API documentation locally, navigate to:
```
https://localhost:5001/swagger
```

---

## Future Improvements

- **Subscription tiers** — enforce content access based on `free` / `standard` / `premium`
- **Recommendation engine** — suggest movies based on watch history and ratings
- **Subtitle support** — multi-language subtitle upload and serving
- **Social features** — like/dislike reviews, comment threads
- **Push notifications** — notify users of new releases in their favorite genres
- **Admin analytics dashboard** — active users, top-rated movies, revenue by tier
- **Rate limiting** — protect public endpoints from abuse (e.g., login brute-force)
- **Refresh token reuse detection** — revoke entire token family on replay attack
- **Docker & docker-compose** — containerized local development setup
- **CI/CD pipeline** — GitHub Actions for build, test, and deploy to Render

---

## License

This project is licensed under the [MIT License](LICENSE).

---

> Built as a graduation project — UIAMovie, 2025.
