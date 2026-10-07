using UIAMovie.Application.DTOs;

namespace UIAMovie.Application.Interfaces.IServices;

public interface IPlaybackService
{
    /// <summary>
    /// Lấy playback information của Movie.
    /// </summary>
    Task<PlaybackResponseDTO?> GetMoviePlaybackAsync(
        Guid movieId,
        Guid? userId);

    /// <summary>
    /// Lấy playback information của TV Show Episode.
    /// </summary>
    Task<PlaybackResponseDTO?> GetEpisodePlaybackAsync(
        Guid tvShowId,
        Guid episodeId,
        Guid? userId);
}