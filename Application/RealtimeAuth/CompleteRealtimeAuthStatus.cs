namespace Application;

public enum CompleteRealtimeAuthStatus
{
    Success = 0,
    UserNotFound = 1,
    NonceNotFoundOrUsed = 2,
    InvalidSignature = 3,
    InvalidPublicKey = 4,
    UnsupportedAlgorithm = 5
}
