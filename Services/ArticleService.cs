using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SmartReader;
using Microsoft.Extensions.Logging;

namespace Anagnostes.Services;

/// <summary>Fetches a URL, strips ads/navigation, and returns clean article text.</summary>
public class ArticleService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<ArticleService> _logger;

    public ArticleService(ILogger<ArticleService> logger)
    {
        _logger = logger;
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Fetches the article at <paramref name="url"/> and returns the title and clean body text.
    /// </summary>
    public async Task<(string Title, string Text)> FetchAsync(string url, CancellationToken ct = default)
    {
        _logger.LogInformation("Article fetch started.");
        var html = await _http.GetStringAsync(url, ct).ConfigureAwait(false);

        var reader = new Reader(url, html);
        var article = await reader.GetArticleAsync().ConfigureAwait(false);

        if (!article.IsReadable || string.IsNullOrWhiteSpace(article.TextContent))
            throw new InvalidOperationException(
                "Could not extract readable article content from the provided URL. " +
                "Please ensure the link points to an article page.");

        var title = string.IsNullOrWhiteSpace(article.Title) ? url : article.Title.Trim();
        var text  = article.TextContent.Trim();

        _logger.LogInformation("Article fetch completed. {CharacterCount} characters extracted.", text.Length);
        return (title, text);
    }

    public void Dispose() => _http.Dispose();
}
