// UIAMovie.Application/AI/Retrieval/IUserContextRetriever.cs
using UIAMovie.Application.AI.Models;

namespace UIAMovie.Application.AI.Retrieval;

public interface IUserContextRetriever
{
    Task<AiUserContext> GetUserContextAsync(Guid? userId);
}