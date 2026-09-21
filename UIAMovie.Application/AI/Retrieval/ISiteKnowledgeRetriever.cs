// UIAMovie.Application/AI/Retrieval/ISiteKnowledgeRetriever.cs
namespace UIAMovie.Application.AI.Retrieval;

public interface ISiteKnowledgeRetriever
{
    Task<string> GetSiteKnowledgeAsync(string message);
}