using Application;
using Microsoft.EntityFrameworkCore;
using Model;

namespace Infrastructure.EF;

public class MailboxRepository(AppDbContext context) : IMailboxRepository
{
    public async Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        return await context.UserMailboxes
            .AsNoTracking()
            .Where(x => x.MailboxAddress == mailboxAddress)
            .Select(x => new MailboxOwner(x.User, x.ExpiresAt))
            .SingleAsync(ctn);
    }

    public async Task<MailboxMap> GetLastMailboxForUserAsync(string user, CancellationToken ctn)
    {
        return await context.UserMailboxes
            .AsNoTracking()
            .Where(x => x.User == user && x.IsCurrent)
            .Select(x => new MailboxMap(x.MailboxAddress, x.ExpiresAt))
            .SingleAsync(ctn);
    }

    public Task CreateMailboxAsync(UserMailbox userMailbox, CancellationToken ctn)
    {
        context.UserMailboxes.Add(userMailbox);
        return Task.CompletedTask;
    }

    public async Task<List<string>> DeleteExpiredMailboxesAsync(CancellationToken ctn)
    {
        var query = context.UserMailboxes.Where(x => x.ExpiresAt <= DateTime.UtcNow);
        var res = await query.Select(x => x.User).Distinct().ToListAsync(ctn);
        await query.ExecuteDeleteAsync(ctn);
        return res;
    }
}