namespace Application;

public interface IRealtimeAuthSignatureVerifier
{
    string Algorithm { get; }
    bool IsValidPublicKey(byte[] publicKey);
    bool VerifySignature(byte[] payload, byte[] signature, byte[] publicKey);
}
