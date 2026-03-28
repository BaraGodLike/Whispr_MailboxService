namespace Model;

public record UserCurrentMailbox(string User, Guid MailboxAddress, DateOnly ExpiresDay);