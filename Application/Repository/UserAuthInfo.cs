namespace Application;

public sealed record UserAuthInfo(string User, string AuthAlg, byte[] PublicKey);
