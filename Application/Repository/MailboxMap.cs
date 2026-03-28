namespace Application;

public readonly record struct MailboxMap(Guid Mailbox, DateTime ExpiresAt);