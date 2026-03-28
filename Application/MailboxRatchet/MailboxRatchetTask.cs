using Model;

namespace Application.MailboxRatchet;

public class MailboxRatchetTask(IMailboxRepository repository)
{
    public async Task Run(CancellationToken ctn)
    {
        var deleted = await repository.DeleteExpiredMailboxesAsync(ctn);
        foreach (var user in deleted)
        {
            await repository.CreateMailboxAsync(
                new UserMailbox(
                    User: user,
                    MailboxAddress: Guid.NewGuid(),
                    ExpiresAt: DateTime.UtcNow + TimeSpan.FromDays(7),
                    IsCurrent: true),
                ctn);
        }
        
    } 
}