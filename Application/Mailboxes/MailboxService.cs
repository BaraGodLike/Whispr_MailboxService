using System.Security.Cryptography;
using System.Text;

namespace Application;

public sealed class MailboxService(
    IMailboxRepository repository,
    IRealtimeAuthNonceStore nonceStore,
    IEnumerable<IRealtimeAuthSignatureVerifier> signatureVerifiers,
    IDateTimeProvider dateTimeProvider)
    : IMailboxService
{
    private const int RealtimeAuthNonceSize = 32;
    private static readonly TimeSpan RealtimeAuthTtl = TimeSpan.FromSeconds(60);
    private static readonly byte[] RealtimeAuthPrefix = "realtime-auth"u8.ToArray();

    public async Task<MailboxOwner?> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        var owner = await repository.GetUserByMailboxAsync(mailboxAddress, ctn);
        if (owner == default)
            return null;

        var today = dateTimeProvider.GetCurrentDate();
        if (MailboxPolicy.IsOwnerMappingActive(today, owner.ExpiresDay))
            return owner;

        return null;
    }

    public async Task<MailboxMap?> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn)
    {
        var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());
        var map = await repository.GetCurrentMailboxForUserAsync(user, schedule.CurrentExpiresDay, ctn);
        if (map != default)
            return map;

        return null;
    }

    public async Task<RegisterUserResult> RegisterUserAsync(string user, string authAlg, byte[] publicKey, CancellationToken ctn)
    {
        var signatureVerifier = signatureVerifiers.FirstOrDefault(v =>
            string.Equals(v.Algorithm, authAlg, StringComparison.OrdinalIgnoreCase));
        if (signatureVerifier is null)
            return new RegisterUserResult(RegisterUserStatus.UnsupportedAlgorithm);

        if (!signatureVerifier.IsValidPublicKey(publicKey))
            return new RegisterUserResult(RegisterUserStatus.InvalidPublicKey);

        var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());
        var created = await repository.RegisterUserAsync(user, authAlg, publicKey, schedule, ctn);
        return new RegisterUserResult(created ? RegisterUserStatus.Success : RegisterUserStatus.AlreadyExists);
    }

    public async Task<RealtimeAuthChallenge?> BeginRealtimeAuthAsync(string user, CancellationToken ctn)
    {
        var authInfo = await repository.GetUserAuthInfoAsync(user, ctn);
        if (authInfo is null)
            return null;

        var nonceBytes = new byte[RealtimeAuthNonceSize];
        RandomNumberGenerator.Fill(nonceBytes);

        var nonce = Convert.ToBase64String(nonceBytes);
        var expiresAtUtc = dateTimeProvider.GetCurrentDateTime().Add(RealtimeAuthTtl);

        await nonceStore.StoreNonceAsync(nonce, user, RealtimeAuthTtl, ctn);

        return new RealtimeAuthChallenge(nonce, expiresAtUtc);
    }

    public async Task<CompleteRealtimeAuthResult> CompleteRealtimeAuthAsync(
        string user,
        string nonce,
        byte[] nonceBytes,
        byte[] signature,
        CancellationToken ctn)
    {
        var authInfo = await repository.GetUserAuthInfoAsync(user, ctn);
        if (authInfo is null)
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.UserNotFound);

        var signatureVerifier = signatureVerifiers.FirstOrDefault(v =>
            string.Equals(v.Algorithm, authInfo.AuthAlg, StringComparison.OrdinalIgnoreCase));
        if (signatureVerifier is null)
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.UnsupportedAlgorithm);

        if (!signatureVerifier.IsValidPublicKey(authInfo.PublicKey))
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.InvalidPublicKey);

        var payload = BuildRealtimeAuthPayload(user, nonceBytes);
        if (!signatureVerifier.VerifySignature(payload, signature, authInfo.PublicKey))
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.InvalidSignature);

        var nonceOwner = await nonceStore.ConsumeNonceAsync(nonce, ctn);
        if (!string.Equals(nonceOwner, user, StringComparison.Ordinal))
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.NonceNotFoundOrUsed);

        var (minExpiresDay, maxExpiresDay) = MailboxPolicy.BuildActiveMailboxWindow(dateTimeProvider.GetCurrentDate());
        var mailboxes = await repository.GetActiveMailboxesForUserAsync(user, minExpiresDay, maxExpiresDay, ctn);
        return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.Success, mailboxes);
    }

    private static byte[] BuildRealtimeAuthPayload(string user, byte[] nonceBytes)
    {
        var userBytes = Encoding.UTF8.GetBytes(user);
        var payload = new byte[RealtimeAuthPrefix.Length + userBytes.Length + nonceBytes.Length];

        Buffer.BlockCopy(RealtimeAuthPrefix, 0, payload, 0, RealtimeAuthPrefix.Length);
        Buffer.BlockCopy(userBytes, 0, payload, RealtimeAuthPrefix.Length, userBytes.Length);
        Buffer.BlockCopy(nonceBytes, 0, payload, RealtimeAuthPrefix.Length + userBytes.Length, nonceBytes.Length);

        return payload;
    }
}
