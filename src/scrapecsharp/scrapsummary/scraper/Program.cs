using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodeHollow.FeedReader;
using Microsoft.Playwright;
using Octokit;

using var httpClient = new HttpClient();
await using var app = await FeedSyncApplication.CreateAsync(httpClient);
await app.RunAsync();

internal sealed class FeedSyncApplication : IAsyncDisposable
{
	private readonly GitHubIssueService _issueService;
	private readonly BlogContentFetcher _contentFetcher;
	private readonly OpenAiSummaryService _summaryService;
	private readonly string _feedSourcePath;

	private FeedSyncApplication(
		GitHubIssueService issueService,
		BlogContentFetcher contentFetcher,
		OpenAiSummaryService summaryService,
		string feedSourcePath)
	{
		_issueService = issueService;
		_contentFetcher = contentFetcher;
		_summaryService = summaryService;
		_feedSourcePath = feedSourcePath;
	}

	public static async Task<FeedSyncApplication> CreateAsync(HttpClient httpClient)
	{
		var repoRoot = RepositoryPaths.FindRepositoryRoot();
		var feedSourcePath = Path.Combine(repoRoot, "feedsource.txt");
		var repository = GitHubRepository.FromEnvironment(AppDefaults.DefaultOwner, AppDefaults.DefaultRepository);
		var issueService = GitHubIssueService.Create(repository);
		var contentFetcher = await BlogContentFetcher.CreateAsync();
		var summaryService = OpenAiSummaryService.Create(httpClient);

		return new FeedSyncApplication(issueService, contentFetcher, summaryService, feedSourcePath);
	}

	public async Task RunAsync(CancellationToken cancellationToken = default)
	{
		var targetIssue = await _issueService.GetLatestOpenIssueAsync(cancellationToken);
		var lastUpdate = await _issueService.GetLastUpdateAsync(targetIssue, cancellationToken);
		Console.WriteLine($"Target issue: #{targetIssue.Number} ({targetIssue.Title})");
		Console.WriteLine($"Last update (UTC): {lastUpdate:O}");

		var seenLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var urls = await File.ReadAllLinesAsync(_feedSourcePath, cancellationToken);

		foreach (var url in urls)
		{
			if (string.IsNullOrWhiteSpace(url))
			{
				continue;
			}

			Console.WriteLine($"Reading feed: {url}");
			CodeHollow.FeedReader.Feed feed;
			try
			{
				feed = await FeedReader.ReadAsync(url);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to read feed {url}: {ex.Message}");
				continue;
			}

			foreach (var item in feed.Items)
			{
				if (string.IsNullOrWhiteSpace(item.Link))
				{
					continue;
				}

				var link = item.Link.Trim();
				if (!seenLinks.Add(link))
				{
					continue;
				}

				var publishedAt = NormalizeToUtc(item.PublishingDate) ?? NormalizeToUtc(item.PublishingDateString);
				if (publishedAt is null || publishedAt <= lastUpdate)
				{
					continue;
				}

				var title = string.IsNullOrWhiteSpace(item.Title) ? link : item.Title.Trim();
				var summary = AppDefaults.SummaryFailedMessage;

				try
				{
					Console.WriteLine($"Fetching content: {link}");
					var blogData = await _contentFetcher.FetchAsync(link, cancellationToken);
					if (!string.IsNullOrWhiteSpace(blogData.Error))
					{
						Console.WriteLine($"fetch-content error: {blogData.Error}");
					}

					if (!string.IsNullOrWhiteSpace(blogData.Content))
					{
						var fetchedTitle = string.IsNullOrWhiteSpace(blogData.Title) ? title : blogData.Title;
						summary = await _summaryService.GetSummaryAsync(link, fetchedTitle, blogData.Content, cancellationToken);
						if (string.IsNullOrWhiteSpace(summary))
						{
							summary = AppDefaults.SummaryFailedMessage;
						}
					}
					else
					{
						Console.WriteLine($"Failed to fetch content for {link}, skipping summarization.");
						summary = AppDefaults.ContentFailedMessage;
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Summarization pipeline error for {link}: {ex.Message}");
					summary = AppDefaults.PipelineFailedMessage;
				}
				finally
				{
					var comment = $"[{title}]({link})  {summary}";
					Console.WriteLine($"Posting comment for article: {title}");
					await _issueService.AddCommentAsync(targetIssue.Number, comment, cancellationToken);
					await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
				}
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _contentFetcher.DisposeAsync();
		_issueService.Dispose();
	}

	private static DateTimeOffset? NormalizeToUtc(DateTime? dateTime)
	{
		if (dateTime is null)
		{
			return null;
		}

		return dateTime.Value.Kind switch
		{
			DateTimeKind.Utc => new DateTimeOffset(dateTime.Value, TimeSpan.Zero),
			DateTimeKind.Local => new DateTimeOffset(dateTime.Value.ToUniversalTime(), TimeSpan.Zero),
			_ => new DateTimeOffset(DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc), TimeSpan.Zero)
		};
	}

	private static DateTimeOffset? NormalizeToUtc(string? dateText)
	{
		if (string.IsNullOrWhiteSpace(dateText))
		{
			return null;
		}

		return DateTimeOffset.TryParse(dateText, out var parsed)
			? parsed.ToUniversalTime()
			: null;
	}
}

internal sealed class GitHubIssueService : IDisposable
{
	private readonly GitHubClient _client;
	private readonly GitHubRepository _repository;

	private GitHubIssueService(GitHubClient client, GitHubRepository repository)
	{
		_client = client;
		_repository = repository;
	}

	public static GitHubIssueService Create(GitHubRepository repository)
	{
		var token = Environment.GetEnvironmentVariable("GH_TOKEN");
		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException("GH_TOKEN environment variable is required.");
		}

		var client = new GitHubClient(new Octokit.ProductHeaderValue("devblogradio-feed-sync"))
		{
			Credentials = new Credentials(token)
		};

		return new GitHubIssueService(client, repository);
	}

	public async Task<Issue> GetLatestOpenIssueAsync(CancellationToken cancellationToken)
	{
		var request = new RepositoryIssueRequest
		{
			State = ItemStateFilter.Open,
			SortProperty = IssueSort.Created,
			SortDirection = SortDirection.Descending
		};

		var options = new ApiOptions
		{
			PageCount = 1,
			PageSize = 1,
			StartPage = 1
		};

		var issues = await _client.Issue.GetAllForRepository(_repository.Owner, _repository.Name, request, options);
		cancellationToken.ThrowIfCancellationRequested();

		return issues.FirstOrDefault() ?? throw new InvalidOperationException("No open issue found.");
	}

	public async Task<DateTimeOffset> GetLastUpdateAsync(Issue issue, CancellationToken cancellationToken)
	{
		var comments = await _client.Issue.Comment.GetAllForIssue(_repository.Owner, _repository.Name, issue.Number);
		cancellationToken.ThrowIfCancellationRequested();

		var latestComment = comments
			.OrderByDescending(comment => comment.CreatedAt)
			.FirstOrDefault();

		return latestComment?.CreatedAt.ToUniversalTime() ?? issue.CreatedAt.ToUniversalTime();
	}

	public async Task AddCommentAsync(int issueNumber, string body, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await _client.Issue.Comment.Create(_repository.Owner, _repository.Name, issueNumber, body);
	}

	public void Dispose()
	{
	}
}

internal sealed class BlogContentFetcher : IAsyncDisposable
{
	private readonly IPlaywright _playwright;
	private readonly IBrowser _browser;

	private BlogContentFetcher(IPlaywright playwright, IBrowser browser)
	{
		_playwright = playwright;
		_browser = browser;
	}

	public static async Task<BlogContentFetcher> CreateAsync()
	{
		var playwright = await Playwright.CreateAsync();
		var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
		{
			Headless = true
		});

		return new BlogContentFetcher(playwright, browser);
	}

	public async Task<BlogContentResult> FetchAsync(string url, CancellationToken cancellationToken)
	{
		var page = await _browser.NewPageAsync();
		try
		{
			await page.GotoAsync(url, new PageGotoOptions
			{
				WaitUntil = WaitUntilState.DOMContentLoaded,
				Timeout = 30000
			});

			cancellationToken.ThrowIfCancellationRequested();
			var title = await page.TitleAsync();

			await page.EvaluateAsync("""
				() => {
				const removeTargets = (root) => {
					root.querySelectorAll('pre, code, img, figure, svg, script, style, video, iframe, table').forEach((element) => element.remove());
				};

				const article = document.querySelector('article[data-clarity-region="article"] div.entry-content');
				if (article) {
					removeTargets(article);
				}
				}
			""");

			var content = await page.EvaluateAsync<string>("""
				() => {
				const removeTargets = (root) => {
					root.querySelectorAll('pre, code, img, figure, svg, script, style, video, iframe, table').forEach((element) => element.remove());
				};

				const article = document.querySelector('article[data-clarity-region="article"] div.entry-content');
				if (article) {
					return article.innerText || '';
				}

				const fallback = document.querySelector('article .entry-content') ||
					document.querySelector('.entry-content') ||
					document.querySelector('article');

				if (fallback) {
					removeTargets(fallback);
					return fallback.innerText || '';
				}

				return '';
				}
			""");

			var cleanContent = CollapseBlankLines(content);
			return new BlogContentResult(title, cleanContent, string.Empty);
		}
		catch (Exception ex)
		{
			return new BlogContentResult(string.Empty, string.Empty, ex.Message);
		}
		finally
		{
			await page.CloseAsync();
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _browser.DisposeAsync();
		_playwright.Dispose();
	}

	private static string CollapseBlankLines(string? content)
	{
		if (string.IsNullOrWhiteSpace(content))
		{
			return string.Empty;
		}

		var builder = new StringBuilder(content.Length);
		var newlineCount = 0;

		foreach (var ch in content.Replace("\r\n", "\n", StringComparison.Ordinal))
		{
			if (ch == '\n')
			{
				newlineCount++;
				if (newlineCount <= 2)
				{
					builder.Append(ch);
				}

				continue;
			}

			newlineCount = 0;
			builder.Append(ch);
		}

		return builder.ToString().Trim();
	}
}

internal sealed class OpenAiSummaryService
{
	private readonly HttpClient _httpClient;
	private readonly string _token;
	private readonly Uri _endpoint;
	private readonly string _deployment;

	private OpenAiSummaryService(HttpClient httpClient, string token, Uri endpoint, string deployment)
	{
		_httpClient = httpClient;
		_token = token;
		_endpoint = endpoint;
		_deployment = deployment;
	}

	public static OpenAiSummaryService Create(HttpClient httpClient)
	{
		var token = Environment.GetEnvironmentVariable("OPENAI_API_TOKEN");
		var openAiBase = Environment.GetEnvironmentVariable("OPENAI_API_BASE");
		var deployment = Environment.GetEnvironmentVariable("OPENAI_API_DEPLOY");

		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException("OPENAI_API_TOKEN environment variable is required.");
		}

		if (string.IsNullOrWhiteSpace(openAiBase))
		{
			throw new InvalidOperationException("OPENAI_API_BASE environment variable is required.");
		}

		if (string.IsNullOrWhiteSpace(deployment))
		{
			throw new InvalidOperationException("OPENAI_API_DEPLOY environment variable is required.");
		}

		var endpoint = new Uri($"https://{openAiBase}.cognitiveservices.azure.com/openai/v1/chat/completions");
		return new OpenAiSummaryService(httpClient, token, endpoint, deployment);
	}

	public async Task<string> GetSummaryAsync(string blogUrl, string blogTitle, string blogContent, CancellationToken cancellationToken)
	{
		var truncatedContent = blogContent.Length > 8000
			? blogContent[..8000] + "\n...(以下省略)"
			: blogContent;

		var prompt = $"""
以下のブログ記事を要約してください。本文が日本語以外である場合、日本語で200文字以内に要約してください。本文が英語で1000words以上ある場合は最初と最後の段落を忠実に日本語翻訳し、段落として記載してください。重要と思われる部分の概要をまとめてください。

タイトル: {blogTitle}
URL: {blogUrl}

本文:
{truncatedContent}
""";

		var payload = new ChatCompletionRequest(
			800,
			0.9,
			0.95,
			0,
			0,
			["##"],
			_deployment,
			[new ChatMessage("user", prompt)]);

		using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
		{
			Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default))
		};
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		request.Headers.Add("api-key", _token);
		request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

		try
		{
			using var response = await _httpClient.SendAsync(request, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return AppDefaults.SummaryFailedMessage;
			}

			await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
			var completion = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(contentStream, JsonOptions.Default, cancellationToken);
			var message = completion?.Choices?.FirstOrDefault()?.Message?.Content;
			return string.IsNullOrWhiteSpace(message) ? AppDefaults.SummaryFailedMessage : message.Trim();
		}
		catch
		{
			return AppDefaults.SummaryFailedMessage;
		}
	}
}

internal static class AppDefaults
{
	public const string DefaultOwner = "tfsugjp";
	public const string DefaultRepository = "devblogradio";
	public const string SummaryFailedMessage = "要約に失敗しました。";
	public const string ContentFailedMessage = "本文の取得に失敗しました。";
	public const string PipelineFailedMessage = "要約処理中にエラーが発生しました。";
}

internal static class RepositoryPaths
{
	public static string FindRepositoryRoot()
	{
		foreach (var startPath in GetStartPaths())
		{
			var current = new DirectoryInfo(startPath);
			while (current is not null)
			{
				if (File.Exists(Path.Combine(current.FullName, "feedsource.txt")))
				{
					return current.FullName;
				}

				current = current.Parent;
			}
		}

		throw new InvalidOperationException("Unable to locate repository root containing feedsource.txt.");
	}

	private static IEnumerable<string> GetStartPaths()
	{
		yield return Directory.GetCurrentDirectory();
		yield return AppContext.BaseDirectory;
	}
}

internal sealed record GitHubRepository(string Owner, string Name)
{
	public static GitHubRepository FromEnvironment(string defaultOwner, string defaultName)
	{
		var repositoryValue = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
		if (string.IsNullOrWhiteSpace(repositoryValue))
		{
			return new GitHubRepository(defaultOwner, defaultName);
		}

		var parts = repositoryValue.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return parts.Length == 2
			? new GitHubRepository(parts[0], parts[1])
			: new GitHubRepository(defaultOwner, defaultName);
	}
}

internal sealed record BlogContentResult(string Title, string Content, string Error);

internal sealed record ChatCompletionRequest(
	int MaxTokens,
	double Temperature,
	double TopP,
	int FrequencyPenalty,
	int PresencePenalty,
	string[] Stop,
	string Model,
	ChatMessage[] Messages);

internal sealed record ChatMessage(string Role, string Content);

internal sealed record ChatCompletionResponse(ChatChoice[]? Choices);

internal sealed record ChatChoice(ChatMessage? Message);

internal static class JsonOptions
{
	public static readonly JsonSerializerOptions Default = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
	};
}
