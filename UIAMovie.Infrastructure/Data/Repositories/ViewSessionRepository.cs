// UIAMovie.Infrastructure/Data/Repositories/ViewSessionRepository.cs

using Microsoft.EntityFrameworkCore;
using Npgsql;
using UIAMovie.Application.Interfaces.IRepositories;
using UIAMovie.Domain.Entities;

namespace UIAMovie.Infrastructure.Data.Repositories;

public class ViewSessionRepository : IViewSessionRepository
{
    private readonly MovieDbContext _context;

    public ViewSessionRepository(MovieDbContext context)
    {
        _context = context;
    }

    // ── MOVIE ───────────────────────────────────────────────────────────────

    public Task<MovieViewSession?> GetMovieSessionAsync(Guid? userId, string? guestId, Guid movieId, DateOnly viewDate)
    {
        var q = _context.MovieViewSessions.AsNoTracking().Where(s => s.MovieId == movieId && s.ViewDate == viewDate);

        if (userId.HasValue)
            return q.FirstOrDefaultAsync(s => s.UserId == userId);

        // Guard: không có guestId thì KHÔNG được khớp "GuestId IS NULL" (sẽ trúng session của user đã login)
        if (string.IsNullOrWhiteSpace(guestId))
            return Task.FromResult<MovieViewSession?>(null);

        return q.FirstOrDefaultAsync(s => s.GuestId == guestId);
    }

    public async Task<bool> TryAddMovieSessionAsync(MovieViewSession session)
    {
        _context.MovieViewSessions.Add(session);
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _context.Entry(session).State = EntityState.Detached;
            return false;
        }
    }

    public async Task AddMovieSecondsAsync(Guid sessionId, int seconds)
    {
        var now = DateTime.UtcNow;
        await _context.MovieViewSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.AccumulatedSeconds, s => s.AccumulatedSeconds + seconds)
                .SetProperty(s => s.LastHeartbeatAt, now));
    }

    public async Task<bool> TryClaimMovieViewAsync(MovieViewSession session)
    {
        var sessionId = session.Id;
        var movieId = session.MovieId;

        await using var tx = await _context.Database.BeginTransactionAsync();
        var flipped = await _context.MovieViewSessions
            .Where(s => s.Id == sessionId && !s.IsCountedAsView)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsCountedAsView, true));
        if (flipped == 0) return false; // request khác đã tính → dispose = rollback

        await _context.Movies
            .Where(m => m.Id == movieId)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.ViewCount, m => m.ViewCount + 1));
        await tx.CommitAsync();
        return true;
    }

    // ── TV SHOW ─────────────────────────────────────────────────────────────

    public Task<TvShowViewSession?> GetTvShowSessionAsync(Guid? userId, string? guestId, Guid tvShowId, DateOnly viewDate)
    {
        var q = _context.TvShowViewSessions.AsNoTracking().Where(s => s.TvShowId == tvShowId && s.ViewDate == viewDate);

        if (userId.HasValue)
            return q.FirstOrDefaultAsync(s => s.UserId == userId);

        if (string.IsNullOrWhiteSpace(guestId))
            return Task.FromResult<TvShowViewSession?>(null);

        return q.FirstOrDefaultAsync(s => s.GuestId == guestId);
    }

    public async Task<bool> TryAddTvShowSessionAsync(TvShowViewSession session)
    {
        _context.TvShowViewSessions.Add(session);
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _context.Entry(session).State = EntityState.Detached;
            return false;
        }
    }

    public async Task AddTvShowSecondsAsync(Guid sessionId, int seconds)
    {
        var now = DateTime.UtcNow;
        await _context.TvShowViewSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.AccumulatedSeconds, s => s.AccumulatedSeconds + seconds)
                .SetProperty(s => s.LastHeartbeatAt, now));
    }

    public async Task<bool> TryClaimTvShowViewAsync(TvShowViewSession session)
    {
        var sessionId = session.Id;
        var tvShowId = session.TvShowId;

        await using var tx = await _context.Database.BeginTransactionAsync();
        var flipped = await _context.TvShowViewSessions
            .Where(s => s.Id == sessionId && !s.IsCountedAsView)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsCountedAsView, true));
        if (flipped == 0) return false;

        await _context.TvShows
            .Where(t => t.Id == tvShowId)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.ViewCount, t => t.ViewCount + 1));
        await tx.CommitAsync();
        return true;
    }

    /// <summary>PostgreSQL SQLSTATE 23505 = unique_violation. Chi tiết Npgsql được giấu ở Infrastructure.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}