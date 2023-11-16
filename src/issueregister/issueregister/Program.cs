// See https://aka.ms/new-console-template for more information
using CodeHollow.FeedReader;
using Octokit;


var _token = Environment.GetEnvironmentVariable("GH_TOKEN");
var client = new GitHubClient(new ProductHeaderValue("issueregister"));
var tokenAuth = new Credentials(_token);
client.Credentials = tokenAuth;

var issuesForOctokit = await client.Issue.GetAllForRepository("tfsugjp", "devblogradio");
var recently = new IssueRequest
{
    Filter = IssueFilter.All,
    State = ItemStateFilter.All,
    Since = DateTimeOffset.Now.Subtract(TimeSpan.FromDays(1))
};
var issues = await client.Issue.GetAllForCurrent(recently);

var feeds = await FeedReader.ReadAsync("https://devblogs.microsoft.com/landingpage/");

if (feeds == null) throw new ArgumentNullException(nameof(feeds));
foreach (var feed in feeds.Items)
{
    Console.WriteLine(feed.Title + " - " + feed.Link);
    var createIssue = new NewIssue(feed.Title);
    var issue = await client.Issue.Create("owner", "name", createIssue);
}

