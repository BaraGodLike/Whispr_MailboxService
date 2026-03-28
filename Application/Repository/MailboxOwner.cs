namespace Application;

public readonly record struct MailboxOwner(string User, DateOnly ExpiresDay);