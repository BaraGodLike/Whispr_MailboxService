using Application;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using System.Text;

namespace UnitTests.Mailboxes;

[TestClass]
public sealed class MailboxServiceTests
{
    private static readonly IRealtimeAuthSignatureVerifier[] DefaultVerifiers = [new Ed25519RealtimeAuthSignatureVerifier()];

    [TestMethod]
    public async Task GetUserByMailboxAsync_ReturnsNull_WhenRepositoryReturnsDefault()
    {
        var repository = new FakeMailboxRepository();
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, new FakeRealtimeAuthNonceStore(), DefaultVerifiers, dateTimeProvider);

        var result = await sut.GetUserByMailboxAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsFalse(result.HasValue);
    }

    [TestMethod]
    public async Task GetUserByMailboxAsync_ReturnsOwner_WhenMappingIsActive()
    {
        var owner = new MailboxOwner("alice", new DateOnly(2026, 5, 1));
        var repository = new FakeMailboxRepository { OwnerResult = owner };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, new FakeRealtimeAuthNonceStore(), DefaultVerifiers, dateTimeProvider);

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
        var sut = new MailboxService(repository, new FakeRealtimeAuthNonceStore(), DefaultVerifiers, dateTimeProvider);

        var result = await sut.GetUserByMailboxAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsFalse(result.HasValue);
    }

    [TestMethod]
    public async Task GetCurrentMailboxForUserAsync_PassesCurrentExpiresDayToRepository()
    {
        var mailbox = new MailboxMap(Guid.NewGuid(), new DateOnly(2026, 5, 1));
        var repository = new FakeMailboxRepository { CurrentMailboxResult = mailbox };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, new FakeRealtimeAuthNonceStore(), DefaultVerifiers, dateTimeProvider);
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
        var sut = new MailboxService(repository, new FakeRealtimeAuthNonceStore(), DefaultVerifiers, dateTimeProvider);

        var result = await sut.GetCurrentMailboxForUserAsync("missing-user", CancellationToken.None);

        Assert.IsFalse(result.HasValue);
    }

    [TestMethod]
    public async Task RegisterUserAsync_PassesArgsAndScheduleToRepository()
    {
        var repository = new FakeMailboxRepository { RegisterUserResult = true };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, new FakeRealtimeAuthNonceStore(), DefaultVerifiers, dateTimeProvider);
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var publicKey = privateKey.GeneratePublicKey().GetEncoded();
        using var cts = new CancellationTokenSource();

        var result = await sut.RegisterUserAsync("alice", "Ed25519", publicKey, cts.Token);

        Assert.AreEqual(RegisterUserStatus.Success, result.Status);
        Assert.AreEqual("alice", repository.LastRegisterUserUser);
        Assert.AreEqual("Ed25519", repository.LastRegisterUserAuthAlg);
        CollectionAssert.AreEqual(publicKey, repository.LastRegisterUserPublicKey!);
        Assert.AreEqual(MailboxPolicy.BuildSchedule(new DateOnly(2026, 4, 25)), repository.LastRegisterUserSchedule);
        Assert.AreEqual(cts.Token, repository.LastCancellationToken);
    }

    [TestMethod]
    public async Task RegisterUserAsync_ReturnsInvalidPublicKey_WhenKeyDoesNotMatchAlgorithm()
    {
        var repository = new FakeMailboxRepository { RegisterUserResult = true };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, new FakeRealtimeAuthNonceStore(), DefaultVerifiers, dateTimeProvider);

        var result = await sut.RegisterUserAsync("alice", "Ed25519", [1, 2, 3, 4], CancellationToken.None);

        Assert.AreEqual(RegisterUserStatus.InvalidPublicKey, result.Status);
        Assert.IsNull(repository.LastRegisterUserUser);
    }

    [TestMethod]
    public async Task BeginRealtimeAuthAsync_ReturnsChallenge_AndStoresNonceForUser()
    {
        var repository = new FakeMailboxRepository
        {
            UserAuthInfoResult = new UserAuthInfo("alice", "Ed25519", new byte[32])
        };
        var nonceStore = new FakeRealtimeAuthNonceStore();
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, nonceStore, DefaultVerifiers, dateTimeProvider);

        var result = await sut.BeginRealtimeAuthAsync("alice", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("alice", repository.LastUserAuthLookupUser);
        Assert.AreEqual("alice", nonceStore.LastStoredUser);
        Assert.AreEqual(result.Nonce, nonceStore.LastStoredNonce);
        Assert.AreEqual(DateTime.Parse("2026-04-25T00:01:00Z").ToUniversalTime(), result.ExpiresAtUtc);
        Assert.AreEqual(TimeSpan.FromSeconds(60), nonceStore.LastStoredTtl);
    }

    [TestMethod]
    public async Task CompleteRealtimeAuthAsync_ReturnsActiveMailboxes_WhenSignatureAndNonceAreValid()
    {
        var nonceBytes = Encoding.UTF8.GetBytes("nonce-123");
        var nonceBase64 = Convert.ToBase64String(nonceBytes);

        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var publicKey = privateKey.GeneratePublicKey().GetEncoded();
        var payload = Encoding.ASCII.GetBytes("realtime-auth")
            .Concat(Encoding.UTF8.GetBytes("alice"))
            .Concat(nonceBytes)
            .ToArray();
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        var signature = signer.GenerateSignature();

        var activeMailboxes = new[]
        {
            new MailboxMap(Guid.NewGuid(), new DateOnly(2026, 4, 30)),
            new MailboxMap(Guid.NewGuid(), new DateOnly(2026, 4, 29))
        };

        var repository = new FakeMailboxRepository
        {
            UserAuthInfoResult = new UserAuthInfo("alice", "Ed25519", publicKey),
            ActiveMailboxesResult = activeMailboxes
        };
        var nonceStore = new FakeRealtimeAuthNonceStore { ConsumeResult = "alice" };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, nonceStore, DefaultVerifiers, dateTimeProvider);

        var result = await sut.CompleteRealtimeAuthAsync(
            "alice",
            nonceBase64,
            nonceBytes,
            signature,
            CancellationToken.None);

        Assert.AreEqual(CompleteRealtimeAuthStatus.Success, result.Status);
        CollectionAssert.AreEquivalent(activeMailboxes, result.Mailboxes!.ToArray());
        Assert.AreEqual(nonceBase64, nonceStore.LastConsumedNonce);
        Assert.AreEqual(new DateOnly(2026, 4, 26), repository.LastActiveMailboxMinExpiresDay);
        Assert.AreEqual(new DateOnly(2026, 5, 1), repository.LastActiveMailboxMaxExpiresDay);
    }

    [TestMethod]
    public async Task CompleteRealtimeAuthAsync_ReturnsInvalidSignature_WhenVerificationFails()
    {
        var nonceBytes = Encoding.UTF8.GetBytes("nonce-123");
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var repository = new FakeMailboxRepository
        {
            UserAuthInfoResult = new UserAuthInfo("alice", "Ed25519", privateKey.GeneratePublicKey().GetEncoded())
        };
        var nonceStore = new FakeRealtimeAuthNonceStore { ConsumeResult = "alice" };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, nonceStore, DefaultVerifiers, dateTimeProvider);

        var result = await sut.CompleteRealtimeAuthAsync(
            "alice",
            Convert.ToBase64String(nonceBytes),
            nonceBytes,
            new byte[64],
            CancellationToken.None);

        Assert.AreEqual(CompleteRealtimeAuthStatus.InvalidSignature, result.Status);
        Assert.IsNull(result.Mailboxes);
        Assert.IsNull(nonceStore.LastConsumedNonce);
    }

    [TestMethod]
    public async Task CompleteRealtimeAuthAsync_ReturnsUnsupportedAlgorithm_WhenVerifierIsMissing()
    {
        var nonceBytes = Encoding.UTF8.GetBytes("nonce-123");
        var repository = new FakeMailboxRepository
        {
            UserAuthInfoResult = new UserAuthInfo("alice", "OtherAlg", new byte[32])
        };
        var nonceStore = new FakeRealtimeAuthNonceStore { ConsumeResult = "alice" };
        var dateTimeProvider = new FakeDateTimeProvider(new DateOnly(2026, 4, 25));
        var sut = new MailboxService(repository, nonceStore, DefaultVerifiers, dateTimeProvider);

        var result = await sut.CompleteRealtimeAuthAsync(
            "alice",
            Convert.ToBase64String(nonceBytes),
            nonceBytes,
            new byte[64],
            CancellationToken.None);

        Assert.AreEqual(CompleteRealtimeAuthStatus.UnsupportedAlgorithm, result.Status);
        Assert.IsNull(nonceStore.LastConsumedNonce);
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
        public UserAuthInfo UserAuthInfoResult { get; init; } = null!;
        public IReadOnlyList<MailboxMap> ActiveMailboxesResult { get; init; } = Array.Empty<MailboxMap>();
        public bool RegisterUserResult { get; init; }
        public string? LastCurrentMailboxUser { get; private set; }
        public DateOnly LastCurrentMailboxExpiresDay { get; private set; }
        public string? LastRegisterUserUser { get; private set; }
        public string? LastRegisterUserAuthAlg { get; private set; }
        public byte[]? LastRegisterUserPublicKey { get; private set; }
        public MailboxSchedule LastRegisterUserSchedule { get; private set; }
        public string? LastUserAuthLookupUser { get; private set; }
        public DateOnly LastActiveMailboxMinExpiresDay { get; private set; }
        public DateOnly LastActiveMailboxMaxExpiresDay { get; private set; }
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

        public Task<bool> RegisterUserAsync(string user, string authAlg, byte[] publicKey, MailboxSchedule schedule, CancellationToken ctn)
        {
            LastRegisterUserUser = user;
            LastRegisterUserAuthAlg = authAlg;
            LastRegisterUserPublicKey = publicKey;
            LastRegisterUserSchedule = schedule;
            LastCancellationToken = ctn;
            return Task.FromResult(RegisterUserResult);
        }

        public Task<UserAuthInfo?> GetUserAuthInfoAsync(string user, CancellationToken ctn)
        {
            LastUserAuthLookupUser = user;
            LastCancellationToken = ctn;
            return Task.FromResult<UserAuthInfo?>(UserAuthInfoResult);
        }

        public Task<IReadOnlyList<MailboxMap>> GetActiveMailboxesForUserAsync(
            string user,
            DateOnly minExpiresDay,
            DateOnly maxExpiresDay,
            CancellationToken ctn)
        {
            LastCurrentMailboxUser = user;
            LastActiveMailboxMinExpiresDay = minExpiresDay;
            LastActiveMailboxMaxExpiresDay = maxExpiresDay;
            LastCancellationToken = ctn;
            return Task.FromResult(ActiveMailboxesResult);
        }

        public Task RotateMailboxesAsync(MailboxSchedule schedule, CancellationToken ctn) => Task.CompletedTask;
    }

    private sealed class FakeRealtimeAuthNonceStore : IRealtimeAuthNonceStore
    {
        public string? LastStoredNonce { get; private set; }
        public string? LastStoredUser { get; private set; }
        public TimeSpan LastStoredTtl { get; private set; }
        public string? LastConsumedNonce { get; private set; }
        public string? ConsumeResult { get; init; }

        public Task StoreNonceAsync(string nonce, string user, TimeSpan ttl, CancellationToken ctn)
        {
            LastStoredNonce = nonce;
            LastStoredUser = user;
            LastStoredTtl = ttl;
            return Task.CompletedTask;
        }

        public Task<string?> ConsumeNonceAsync(string nonce, CancellationToken ctn)
        {
            LastConsumedNonce = nonce;
            return Task.FromResult(ConsumeResult);
        }
    }
}
