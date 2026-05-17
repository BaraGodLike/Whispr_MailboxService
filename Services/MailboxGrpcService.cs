using Application;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Services;

public sealed class MailboxGrpcService(IMailboxService mailboxService) : MailboxApi.MailboxApiBase
{
    public override async Task<MailboxResponse> GetMailbox(GetMailboxRequest request, ServerCallContext context)
    {
        var user = ValidateUser(request.User);
        var mailboxMap = await mailboxService.GetCurrentMailboxForUserAsync(user, context.CancellationToken);

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
            User = mailboxOwner.Value.User
        };
    }

    public override async Task<Empty> CreateMailbox(CreateMailboxRequest request, ServerCallContext context)
    {
        var user = ValidateUser(request.User);
        await mailboxService.CreateMailboxAsync(user, context.CancellationToken);
        return new Empty();
    }

    private static string ValidateUser(string user)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "User is required."));
        }

        return user;
    }

    private static MailboxResponse CreateMailboxResponse(MailboxMap mailbox) =>
        new()
        {
            MailboxAddress = mailbox.Mailbox.ToString(),
            RefreshAfterUtc = Timestamp.FromDateTime(MailboxPolicy.GetClientRefreshAfterUtc(mailbox.ExpiresDay))
        };
}
