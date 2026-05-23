using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Application;

public sealed class Ed25519RealtimeAuthSignatureVerifier : IRealtimeAuthSignatureVerifier
{
    public string Algorithm => "Ed25519";

    public bool IsValidPublicKey(byte[] publicKey)
    {
        try
        {
            _ = new Ed25519PublicKeyParameters(publicKey, 0);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public bool VerifySignature(byte[] payload, byte[] signature, byte[] publicKey)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(payload, 0, payload.Length);
        return verifier.VerifySignature(signature);
    }
}
