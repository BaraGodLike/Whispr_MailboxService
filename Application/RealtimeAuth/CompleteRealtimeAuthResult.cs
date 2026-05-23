namespace Application;

public sealed record CompleteRealtimeAuthResult(
    CompleteRealtimeAuthStatus Status,
    IReadOnlyList<MailboxMap>? Mailboxes = null);
