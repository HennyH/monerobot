namespace MoneroBot.Daemon.Features;

using System.Text.RegularExpressions;
using Fider;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public record GetDonationComments(int PostNumber);

public record DonationComment(int CommentId, string Content);

public interface IGetDonationCommentsHandler
{
    public Task<List<DonationComment>> HandleAsync(GetDonationComments request, CancellationToken token = default);
}

public static class DonationTextRegexes
{
    public static readonly Regex LegacyText = new Regex(
        @"^bounty\s+increased\s+by\s+[\d.]+\s+XMR\s+Total\s+Bounty:\s+[\d.]+\s+XMR$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

    public static readonly Regex CurrentText = new Regex(
        @"^bounty\s+increased\s+by\s+[\d.]+\s+XMR\s+[📨💰⏳]+\s+Total\s+Bounty:\s+[\d.]+\s+XMR$",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
}

public class GetDonationCommentsHandler : IGetDonationCommentsHandler
{
    private readonly IFiderApiClient fider;
    private readonly ILogger<GetDonationCommentsHandler> logger;
    private readonly DaemonOptions options;

    public GetDonationCommentsHandler(ILogger<GetDonationCommentsHandler> logger, IOptions<DaemonOptions> options, IFiderApiClient fider)
    {
        this.logger = logger;
        this.fider = fider;
        this.options = options.Value;
    }

    public async Task<List<DonationComment>> HandleAsync(GetDonationComments request, CancellationToken token = default)
    {
        var post = await this.fider.GetPostAsync(request.PostNumber, token);
        var comments = await this.fider.ListCommentsAsync(post.Number, post.CommentsCount, token);

        var donations = new List<DonationComment>();
        foreach (var comment in comments)
        {
            if (comment.User.Id != this.options.FiderMoneroBotUserId)
            {
                continue;
            }

            if (comment.Content.Contains("XMR") is false)
            {
                continue;
            }

            var regexes = new[] { DonationTextRegexes.LegacyText, DonationTextRegexes.CurrentText };
            var match = regexes
                .Select(r => r.Match(comment.Content))
                .FirstOrDefault(m => m is {Success: true});
            if (match is not null)
            {
                var donation = new DonationComment(
                    CommentId: comment.Id,
                    Content: comment.Content);
                donations.Add(donation);
            }
            else
            {
                this.logger.LogError(
                    "Failed to parse and extract the amount/total from the comment {@Comment} which is expected to be a donation comment for post {@Post} given that it contained the phrase 'XMR' and was made by the monero bot",
                    comment,
                    post);
            }
        }

        return donations;
    }
}
