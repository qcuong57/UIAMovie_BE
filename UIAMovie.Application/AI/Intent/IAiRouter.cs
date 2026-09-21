// UIAMovie.Application/AI/Intent/IAiRouter.cs
using UIAMovie.Application.AI.Models;

namespace UIAMovie.Application.AI.Intent;

public interface IAiRouter
{
    Task<AiRoute> RouteAsync(string message, IEnumerable<string>? recentHistory = null);
}