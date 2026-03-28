namespace Model;

public record UserMailbox(string User, Guid MailboxAddress, DateOnly ExpiresDay);