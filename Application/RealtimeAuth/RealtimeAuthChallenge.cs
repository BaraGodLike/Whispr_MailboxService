namespace Application;

public sealed record RealtimeAuthChallenge(string Nonce, DateTime ExpiresAtUtc);
