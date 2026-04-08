namespace Application;

public record struct MailboxOwner(string User, DateOnly ExpiresDay);