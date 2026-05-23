using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Application;

public sealed class MailboxService(
    IMailboxRepository repository,
    IRealtimeAuthNonceStore nonceStore,
    IEnumerable<IRealtimeAuthSignatureVerifier> signatureVerifiers,
    IDateTimeProvider dateTimeProvider,
    ILogger<MailboxService> logger)
    : IMailboxService
{
    private const int RealtimeAuthNonceSize = 32;
    private static readonly TimeSpan RealtimeAuthTtl = TimeSpan.FromSeconds(60);
    private static readonly byte[] RealtimeAuthPrefix = "realtime-auth"u8.ToArray();

    public async Task<MailboxOwner?> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        var owner = await repository.GetUserByMailboxAsync(mailboxAddress, ctn);
        if (owner == default)
        {
            logger.LogWarning("Owner lookup returned no mailbox owner.");
            return null;
        }

        var today = dateTimeProvider.GetCurrentDate();
        if (MailboxPolicy.IsOwnerMappingActive(today, owner.ExpiresDay))
            return owner;

        logger.LogWarning("Owner mapping is expired.");
        return null;
    }

    public async Task<MailboxMap?> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn)
    {
        var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());
        var map = await repository.GetCurrentMailboxForUserAsync(user, schedule.CurrentExpiresDay, ctn);
        if (map != default)
            return map;

        logger.LogWarning("Current mailbox lookup returned no mailbox.");
        return null;
    }

    public Task<bool> RegisterUserAsync(string user, string authAlg, byte[] publicKey, CancellationToken ctn)
    {
        var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());
        return repository.RegisterUserAsync(user, authAlg, publicKey, schedule, ctn);
    }

    public async Task<RealtimeAuthChallenge?> BeginRealtimeAuthAsync(string user, CancellationToken ctn)
    {
        var authInfo = await repository.GetUserAuthInfoAsync(user, ctn);
        if (authInfo is null)
        {
            logger.LogWarning("Realtime auth begin returned no user auth info.");
            return null;
        }

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
        {
            logger.LogWarning("Realtime auth completion returned no user auth info.");
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.UserNotFound);
        }

        if (authInfo.PublicKey.Length != 32)
        {
            logger.LogError("Realtime auth public key has invalid length: {Length}.", authInfo.PublicKey.Length);
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.InvalidPublicKey);
        }

        var signatureVerifier = signatureVerifiers.FirstOrDefault(v =>
            string.Equals(v.Algorithm, authInfo.AuthAlg, StringComparison.OrdinalIgnoreCase));
        if (signatureVerifier is null)
        {
            logger.LogError("Realtime auth algorithm is not supported: {Algorithm}.", authInfo.AuthAlg);
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.UnsupportedAlgorithm);
        }

        var payload = BuildRealtimeAuthPayload(user, nonceBytes);
        bool isSignatureValid;
        try
        {
            isSignatureValid = signatureVerifier.VerifySignature(payload, signature, authInfo.PublicKey);
        }
        catch (ArgumentException)
        {
            logger.LogError("Realtime auth public key bytes are not valid for algorithm {Algorithm}.", authInfo.AuthAlg);
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.InvalidPublicKey);
        }

        if (!isSignatureValid)
        {
            logger.LogWarning("Realtime auth signature verification failed.");
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.InvalidSignature);
        }

        var nonceOwner = await nonceStore.ConsumeNonceAsync(nonce, ctn);
        if (!string.Equals(nonceOwner, user, StringComparison.Ordinal))
        {
            logger.LogWarning("Realtime auth nonce was missing, already used, or bound to another user.");
            return new CompleteRealtimeAuthResult(CompleteRealtimeAuthStatus.NonceNotFoundOrUsed);
        }

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
