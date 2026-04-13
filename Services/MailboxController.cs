using Application;
using Microsoft.AspNetCore.Mvc;

namespace Services;

[ApiController]
[Route("[controller]")]
public class MailboxController(IMailboxService mailboxService) : ControllerBase
{
    [HttpPost("mb")]
    public async Task<ActionResult> GetMailboxAsync([FromBody] string user, CancellationToken ctn)
    {
        var mailboxMap = await mailboxService.GetCurrentMailboxForUserAsync(user, ctn);
        return mailboxMap is null
            ? NotFound(new { Error = "User not found." })
            : Ok(CreateMailboxDto(mailboxMap.Value));
    }

    [HttpPost("user")]
    public async Task<ActionResult> GetUserAsync([FromBody] Guid mailbox, CancellationToken ctn)
    {
        var mailboxOwner = await mailboxService.GetUserByMailboxAsync(mailbox, ctn);
        return mailboxOwner is null
            ? NotFound(new { Error = "User with this mailbox not found." })
            : Ok(new { User = mailboxOwner.Value.User });
    }

    [HttpPost("new")]
    public async Task<ActionResult> CreateNewMailboxAsync([FromBody] string user, CancellationToken ctn)
    {
        var mailbox = await mailboxService.CreateMailboxAsync(user, ctn);
        return Created();
    }

    private static MailboxDto CreateMailboxDto(MailboxMap mailbox) =>
        new(
            MailboxAddress: mailbox.Mailbox,
            RefreshAfter: MailboxPolicy.GetClientRefreshAfterUtc(mailbox.ExpiresDay));
}
