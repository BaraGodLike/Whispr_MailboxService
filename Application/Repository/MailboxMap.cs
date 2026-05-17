namespace Application;

public readonly record struct MailboxMap(Guid Mailbox, DateOnly ExpiresDay);