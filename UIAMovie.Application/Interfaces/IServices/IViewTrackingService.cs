// UIAMovie.Application/Interfaces/IServices/IViewTrackingService.cs

using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces.IServices;

public interface IViewTrackingService
{
    /// <exception cref="KeyNotFoundException">Phim không tồn tại.</exception>
    Task<HeartbeatResultDTO> RecordMovieHeartbeatAsync(Guid? userId, string? guestId, HeartbeatDTO dto);

    /// <exception cref="KeyNotFoundException">TV show không tồn tại.</exception>
    Task<HeartbeatResultDTO> RecordTvShowHeartbeatAsync(Guid? userId, string? guestId, TvShowHeartbeatDTO dto);
}