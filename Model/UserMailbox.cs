namespace Model;

public record UserMailbox(
    string User,
    Guid MailboxAddress,
    DateTime ExpiresAt,
    bool IsCurrent);