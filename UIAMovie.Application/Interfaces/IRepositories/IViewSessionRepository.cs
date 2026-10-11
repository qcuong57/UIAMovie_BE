// UIAMovie.Application/Interfaces/IRepositories/IViewSessionRepository.cs

using UIAMovie.Domain.Entities;

namespace UIAMovie.Application.Interfaces.IRepositories;

/// <summary>
/// Truy cập MovieViewSession / TvShowViewSession (đếm view bằng heartbeat).
/// Mọi thao tác ghi sau khi session đã tồn tại đều là UPDATE nguyên tử trong DB
/// (không đọc-sửa-ghi trên entity) nên heartbeat song song không ghi đè nhau.
/// </summary>
public interface IViewSessionRepository
{
    /// <summary>
    /// Lấy session (không tracking) của user đã login hoặc guest trong ngày.
    /// guestId chỉ dùng khi userId = null; cả hai đều rỗng → trả null.
    /// </summary>
    Task<MovieViewSession?> GetMovieSessionAsync(Guid? userId, string? guestId, Guid movieId, DateOnly viewDate);

    /// <summary>
    /// Thêm session mới. Trả FALSE nếu đã tồn tại (request khác vừa tạo — vi phạm unique index);
    /// khi đó caller nên load lại bằng GetMovieSessionAsync.
    /// </summary>
    Task<bool> TryAddMovieSessionAsync(MovieViewSession session);

    Task<TvShowViewSession?> GetTvShowSessionAsync(Guid? userId, string? guestId, Guid tvShowId, DateOnly viewDate);
    Task<bool> TryAddTvShowSessionAsync(TvShowViewSession session);

    /// <summary>Cộng giây nguyên tử (AccumulatedSeconds += seconds, LastHeartbeatAt = now) — 1 câu UPDATE duy nhất.</summary>
    Task AddMovieSecondsAsync(Guid sessionId, int seconds);
    Task AddTvShowSecondsAsync(Guid sessionId, int seconds);

    /// <summary>
    /// "Giành quyền" tính view: lật IsCountedAsView false→true bằng UPDATE có điều kiện rồi tăng ViewCount
    /// trong CÙNG transaction. Trả TRUE nếu request này là request đã lật (view được cộng đúng 1 lần,
    /// kể cả khi nhiều heartbeat song song).
    /// </summary>
    Task<bool> TryClaimMovieViewAsync(MovieViewSession session);
    Task<bool> TryClaimTvShowViewAsync(TvShowViewSession session);
}