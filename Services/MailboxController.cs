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
        return Ok((await repository.GetCurrentMailboxForUserAsync(user, ctn)).Mailbox);
    }
    
    [HttpPost("user")]
    public async Task<ActionResult> GetMailbox(Guid mailbox, CancellationToken ctn)
    {
        return Ok((await repository.GetUserByMailboxAsync(mailbox, ctn)).User);
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
}