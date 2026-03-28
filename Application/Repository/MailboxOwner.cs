namespace Application;

public readonly record struct MailboxOwner(string User, DateTime ExpiresAt);