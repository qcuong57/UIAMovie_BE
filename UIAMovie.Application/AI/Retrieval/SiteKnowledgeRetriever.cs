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

    public async Task<string> GetSiteKnowledgeAsync(string message)
    {
        // null = không gắn với user nào: chatbot chỉ cần danh sách gói đang bán, không cần kiểm tra điều kiện mua
        var plans = (await _paymentService.GetSubscriptionPlansAsync(null)).ToList();
        return MoviePrompts.SelectSiteKnowledge(message, plans);
    }
}