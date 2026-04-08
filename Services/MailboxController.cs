using Application;
using Microsoft.AspNetCore.Mvc;
using Model;

namespace Services;

[ApiController]
[Route("[controller]")]
public class MailboxController(IMailboxRepository repository) : ControllerBase
{
    [HttpPost("mb")]
    public async Task<ActionResult> GetMailboxAsync([FromBody] string user, CancellationToken ctn)
    {
        var mailboxMap = await repository.GetCurrentMailboxForUserAsync(user, ctn);
        return mailboxMap == default
            ? NotFound(new {Error = "User not found."})
            : Ok(CreateMailboxDto(mailboxMap));
    }
    
    [HttpPost("user")]
    public async Task<ActionResult> GetUserAsync([FromBody] Guid mailbox, CancellationToken ctn)
    {
        var mailboxOwner = await repository.GetUserByMailboxAsync(mailbox, ctn);
        return mailboxOwner == default
            ? NotFound(new {Error = "User with this mailbox not found."})
            : Ok(new {User = mailboxOwner.User});
    }

    [HttpPost("new")]
    public async Task<ActionResult> CreateNewMailboxAsync([FromBody] string user, CancellationToken ctn)
    {
        await repository.CreateMailboxAsync(
            new UserMailbox(
                user,
                MailboxAddress: Guid.NewGuid(),
                ExpiresDay: DateOnly.FromDateTime(DateTime.Today + TimeSpan.FromDays(7))),
            ctn);
        return Created();
    }

    private static MailboxDto CreateMailboxDto(MailboxMap map)
    {
        return new MailboxDto(
            MailboxAddress: map.Mailbox,
            ExpiresAt: map.ExpiresDay.AddDays(-6).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }
}