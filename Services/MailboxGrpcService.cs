using Application;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Services;

public sealed class MailboxGrpcService(IMailboxService mailboxService) : MailboxApi.MailboxApiBase
{
    public override async Task<MailboxResponse> GetMailbox(GetMailboxRequest request, ServerCallContext context)
    {
        var userId = ValidateUserId(request.UserId);
        var mailboxMap = await mailboxService.GetCurrentMailboxForUserAsync(userId, context.CancellationToken);

        if (mailboxMap is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "User not found."));
        }

        return CreateMailboxResponse(mailboxMap.Value);
    }

    public override async Task<GetUserResponse> GetUser(GetUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Mailbox, out var mailbox))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Mailbox must be a valid GUID."));
        }

        var mailboxOwner = await mailboxService.GetUserByMailboxAsync(mailbox, context.CancellationToken);
        if (mailboxOwner is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "User with this mailbox not found."));
        }

        return new GetUserResponse
        {
            UserId = mailboxOwner.Value.User
        };
    }

    public override async Task<Empty> RegisterUser(RegisterUserRequest request, ServerCallContext context)
    {
        var userId = ValidateUserId(request.UserId);
        var authAlg = ValidateAuthAlgorithm(request.AuthAlg);
        var publicKey = ValidatePublicKey(request.PublicKey);

        var result = await mailboxService.RegisterUserAsync(userId, authAlg, publicKey, context.CancellationToken);
        return result.Status switch
        {
            RegisterUserStatus.Success => new Empty(),
            RegisterUserStatus.AlreadyExists => throw new RpcException(new Status(StatusCode.AlreadyExists,
                "User already exists.")),
            RegisterUserStatus.UnsupportedAlgorithm => throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Auth algorithm is not supported.")),
            RegisterUserStatus.InvalidPublicKey => throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Public key is invalid for the selected auth algorithm.")),
            _ => throw new RpcException(new Status(StatusCode.Internal, "Unexpected register user result."))
        };
    }

    public override async Task<BeginRealtimeAuthResponse> BeginRealtimeAuth(
        BeginRealtimeAuthRequest request,
        ServerCallContext context)
    {
        var userId = ValidateUserId(request.UserId);
        var challenge = await mailboxService.BeginRealtimeAuthAsync(userId, context.CancellationToken);
        if (challenge is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "User not found."));
        }

        return new BeginRealtimeAuthResponse
        {
            Nonce = challenge.Nonce,
            ExpAt = Timestamp.FromDateTime(challenge.ExpiresAtUtc)
        };
    }

    public override async Task<CompleteRealtimeAuthResponse> CompleteRealtimeAuth(
        CompleteRealtimeAuthRequest request,
        ServerCallContext context)
    {
        var userId = ValidateUserId(request.UserId);
        ValidateRequestedAlgorithm(request.Alg);

        if (request.Signature.IsEmpty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Signature is required."));
        }

        var nonceBytes = DecodeNonce(request.Nonce);
        var result = await mailboxService.CompleteRealtimeAuthAsync(
            userId,
            request.Nonce,
            nonceBytes,
            request.Signature.ToByteArray(),
            context.CancellationToken);

        return result.Status switch
        {
            CompleteRealtimeAuthStatus.Success => new CompleteRealtimeAuthResponse
            {
                Mailboxes = { result.Mailboxes!.Select(CreateMailboxResponse) }
            },
            CompleteRealtimeAuthStatus.UserNotFound => throw new RpcException(new Status(StatusCode.NotFound, "User not found.")),
            CompleteRealtimeAuthStatus.InvalidSignature => throw new RpcException(new Status(StatusCode.Unauthenticated, "Signature verification failed.")),
            CompleteRealtimeAuthStatus.NonceNotFoundOrUsed => throw new RpcException(new Status(StatusCode.FailedPrecondition, "Nonce was not found, expired, or already used.")),
            CompleteRealtimeAuthStatus.InvalidPublicKey => throw new RpcException(new Status(StatusCode.FailedPrecondition, "Stored public key is invalid.")),
            CompleteRealtimeAuthStatus.UnsupportedAlgorithm => throw new RpcException(new Status(StatusCode.FailedPrecondition, "Stored auth algorithm is not supported.")),
            _ => throw new RpcException(new Status(StatusCode.Internal, "Unexpected realtime auth result."))
        };
    }

    private static string ValidateUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "User ID is required."));
        }

        return userId;
    }

    private static void ValidateRequestedAlgorithm(string algorithm)
    {
        if (string.IsNullOrWhiteSpace(algorithm))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Auth algorithm is required."));
        }
    }

    private static byte[] DecodeNonce(string nonce)
    {
        try
        {
            return Convert.FromBase64String(nonce);
        }
        catch (FormatException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Nonce must be a valid base64 string."));
        }
    }

    private static string ValidateAuthAlgorithm(string authAlg)
    {
        if (string.IsNullOrWhiteSpace(authAlg))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Auth algorithm is required."));
        }

        return authAlg;
    }

    private static byte[] ValidatePublicKey(Google.Protobuf.ByteString publicKey)
    {
        if (publicKey.IsEmpty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Public key is required."));
        }

        return publicKey.ToByteArray();
    }

    private static MailboxResponse CreateMailboxResponse(MailboxMap mailbox) =>
        new()
        {
            MailboxAddress = mailbox.Mailbox.ToString(),
            RefreshAfterUtc = Timestamp.FromDateTime(MailboxPolicy.GetClientRefreshAfterUtc(mailbox.ExpiresDay))
        };
}
