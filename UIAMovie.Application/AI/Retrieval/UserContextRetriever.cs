// UIAMovie.Application/AI/Retrieval/UserContextRetriever.cs
using UIAMovie.Application.AI.Models;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Services;

namespace UIAMovie.Application.AI.Retrieval;

public class UserContextRetriever : IUserContextRetriever
{
    private readonly IPaymentService _paymentService;
    private readonly IMovieService _movieService;

    public UserContextRetriever(IPaymentService paymentService, IMovieService movieService)
    {
        _paymentService = paymentService;
        _movieService = movieService;
    }

    public async Task<AiUserContext> GetUserContextAsync(Guid? userId)
    {
        if (!userId.HasValue || userId == Guid.Empty)
            return AiUserContext.Guest;

        var subStatus = await _paymentService.GetSubscriptionStatusAsync(userId.Value);
        var watchHistory = await _movieService.GetWatchHistoryAsync(userId.Value);
        var favorites = await _movieService.GetFavoritesAsync(userId.Value);

        return AiUserContext.From(
            subStatus,
            watchHistory.Select(h => h.MovieTitle),
            favorites.Select(f => f.MovieTitle));
    }
}