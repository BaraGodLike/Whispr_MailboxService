using Application;

namespace UnitTests.Mailboxes;

[TestClass]
public sealed class MailboxServiceTests
{
    [TestMethod]
    public async Task GetUserByMailboxAsync_ReturnsNull_WhenRepositoryReturnsDefault()
    {
        var repository = new FakeMailboxRepository();
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, dateTimeProvider);

        var result = await sut.GetUserByMailboxAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsFalse(result.HasValue);
    }

    [TestMethod]
    public async Task GetUserByMailboxAsync_ReturnsOwner_WhenMappingIsActive()
    {
        var owner = new MailboxOwner("alice", new DateOnly(2026, 5, 1));
        var repository = new FakeMailboxRepository { OwnerResult = owner };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, dateTimeProvider);

        var result = await sut.GetUserByMailboxAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsTrue(result.HasValue);
        Assert.AreEqual(owner, result.Value);
    }

    [TestMethod]
    public async Task GetUserByMailboxAsync_ReturnsNull_WhenMappingExpired()
    {
        var repository = new FakeMailboxRepository
        {
            OwnerResult = new MailboxOwner("alice", new DateOnly(2026, 4, 25))
        };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, dateTimeProvider);

        var result = await sut.GetUserByMailboxAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsFalse(result.HasValue);
    }

    [TestMethod]
    public async Task GetCurrentMailboxForUserAsync_PassesCurrentExpiresDayToRepository()
    {
        var mailbox = new MailboxMap(Guid.NewGuid(), new DateOnly(2026, 5, 1));
        var repository = new FakeMailboxRepository { CurrentMailboxResult = mailbox };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, dateTimeProvider);
        using var cts = new CancellationTokenSource();

        var result = await sut.GetCurrentMailboxForUserAsync("alice", cts.Token);

        Assert.IsTrue(result.HasValue);
        Assert.AreEqual(mailbox, result.Value);
        Assert.AreEqual("alice", repository.LastCurrentMailboxUser);
        Assert.AreEqual(new DateOnly(2026, 5, 1), repository.LastCurrentMailboxExpiresDay);
        Assert.AreEqual(cts.Token, repository.LastCancellationToken);
    }

    [TestMethod]
    public async Task GetCurrentMailboxForUserAsync_ReturnsNull_WhenRepositoryReturnsDefault()
    {
        var repository = new FakeMailboxRepository();
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, dateTimeProvider);

        var result = await sut.GetCurrentMailboxForUserAsync("missing-user", CancellationToken.None);

        Assert.IsFalse(result.HasValue);
    }

    [TestMethod]
    public async Task CreateMailboxAsync_PassesBuiltScheduleToRepository()
    {
        var mailbox = new MailboxMap(Guid.NewGuid(), new DateOnly(2026, 5, 1));
        var repository = new FakeMailboxRepository { CreateMailboxResult = mailbox };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, dateTimeProvider);
        using var cts = new CancellationTokenSource();

        var result = await sut.CreateMailboxAsync("alice", cts.Token);

        Assert.AreEqual(mailbox, result);
        Assert.AreEqual("alice", repository.LastCreateMailboxUser);
        Assert.AreEqual(MailboxPolicy.BuildSchedule(new DateOnly(2026, 4, 25)), repository.LastCreateMailboxSchedule);
        Assert.AreEqual(cts.Token, repository.LastCancellationToken);
    }

    private sealed class FakeDateTimeProvider(DateOnly currentDate) : IDateTimeProvider
    {
        public DateOnly GetCurrentDate() => currentDate;

        public DateTime GetCurrentDateTime() => currentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }

    private sealed class FakeMailboxRepository : IMailboxRepository
    {
        public MailboxOwner OwnerResult { get; init; }
        public MailboxMap CurrentMailboxResult { get; init; }
        public MailboxMap CreateMailboxResult { get; init; }
        public string? LastCurrentMailboxUser { get; private set; }
        public DateOnly LastCurrentMailboxExpiresDay { get; private set; }
        public string? LastCreateMailboxUser { get; private set; }
        public MailboxSchedule LastCreateMailboxSchedule { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
        {
            LastCancellationToken = ctn;
            return Task.FromResult(OwnerResult);
        }

        public Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, DateOnly expiresDay, CancellationToken ctn)
        {
            LastCurrentMailboxUser = user;
            LastCurrentMailboxExpiresDay = expiresDay;
            LastCancellationToken = ctn;
            return Task.FromResult(CurrentMailboxResult);
        }

        public Task<MailboxMap> CreateMailboxAsync(string user, MailboxSchedule schedule, CancellationToken ctn)
        {
            LastCreateMailboxUser = user;
            LastCreateMailboxSchedule = schedule;
            LastCancellationToken = ctn;
            return Task.FromResult(CreateMailboxResult);
        }

        public Task RotateMailboxesAsync(MailboxSchedule schedule, CancellationToken ctn) => Task.CompletedTask;
    }
}
