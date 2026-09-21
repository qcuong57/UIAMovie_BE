// UIAMovie.Application/AI/Retrieval/SiteKnowledgeRetriever.cs
using UIAMovie.Application.Services;

namespace UIAMovie.Application.AI.Retrieval;

public class SiteKnowledgeRetriever : ISiteKnowledgeRetriever
{
    private readonly IPaymentService _paymentService;

    public SiteKnowledgeRetriever(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public Task<string> GetSiteKnowledgeAsync(string message)
    {
        var plans = _paymentService.GetSubscriptionPlans().ToList();
        var context = MoviePrompts.SelectSiteKnowledge(message, plans);
        return Task.FromResult(context);
    }
}