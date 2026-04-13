namespace Services;

public readonly record struct MailboxDto(Guid MailboxAddress, DateTime RefreshAfter);
