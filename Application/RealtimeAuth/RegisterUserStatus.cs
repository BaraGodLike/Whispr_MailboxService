namespace Application;

public enum RegisterUserStatus
{
    Success = 0,
    AlreadyExists = 1,
    UnsupportedAlgorithm = 2,
    InvalidPublicKey = 3
}
