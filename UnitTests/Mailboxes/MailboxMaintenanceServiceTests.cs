using Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTests.Mailboxes;

[TestClass]
public sealed class MailboxMaintenanceServiceTests
{
    [TestMethod]
    public async Task RunDailyRotationAsync_PassesBuiltScheduleToRepository()
    {
        var repository = new FakeMailboxRepository();
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxMaintenanceService(
            repository,
            dateTimeProvider,
            NullLogger<MailboxMaintenanceService>.Instance);
        using var cts = new CancellationTokenSource();

        await sut.RunDailyRotationAsync(cts.Token);

        Assert.AreEqual(1, repository.RotateCallCount);
        Assert.AreEqual(MailboxPolicy.BuildSchedule(new DateOnly(2026, 4, 25)), repository.LastRotationSchedule);
        Assert.AreEqual(cts.Token, repository.LastCancellationToken);
    }

    [TestMethod]
    public async Task RunDailyRotationAsync_PropagatesRepositoryException()
    {
        var expectedException = new InvalidOperationException("rotation failed");
        var repository = new FakeMailboxRepository { RotateException = expectedException };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxMaintenanceService(
            repository,
            dateTimeProvider,
            NullLogger<MailboxMaintenanceService>.Instance);

        try
        {
            await sut.RunDailyRotationAsync(CancellationToken.None);
            Assert.Fail("Expected repository exception to be propagated.");
        }
        catch (InvalidOperationException actualException)
        {
            Assert.AreSame(expectedException, actualException);
        }
    }

    private sealed class FakeDateTimeProvider(DateOnly currentDate) : IDateTimeProvider
    {
        public DateOnly GetCurrentDate() => currentDate;

        public DateTime GetCurrentDateTime() => currentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
    }

    private sealed class FakeMailboxRepository : IMailboxRepository
    {
        public Exception? RotateException { get; init; }
        public int RotateCallCount { get; private set; }
        public MailboxSchedule LastRotationSchedule { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn) =>
            Task.FromResult(default(MailboxOwner));

        public Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, DateOnly expiresDay, CancellationToken ctn) =>
            Task.FromResult(default(MailboxMap));

        public Task<bool> RegisterUserAsync(string user, string authAlg, byte[] publicKey, MailboxSchedule schedule, CancellationToken ctn) =>
            Task.FromResult(false);

        public Task<UserAuthInfo?> GetUserAuthInfoAsync(string user, CancellationToken ctn) =>
            Task.FromResult<UserAuthInfo?>(null);

        public Task<IReadOnlyList<MailboxMap>> GetActiveMailboxesForUserAsync(
            string user,
            DateOnly minExpiresDay,
            DateOnly maxExpiresDay,
            CancellationToken ctn) =>
            Task.FromResult<IReadOnlyList<MailboxMap>>(Array.Empty<MailboxMap>());

        public Task RotateMailboxesAsync(MailboxSchedule schedule, CancellationToken ctn)
        {
            RotateCallCount++;
            LastRotationSchedule = schedule;
            LastCancellationToken = ctn;

            return RotateException is not null ? Task.FromException(RotateException) : Task.CompletedTask;
        }
    }
}
