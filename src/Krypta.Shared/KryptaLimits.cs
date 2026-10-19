namespace Krypta.Shared;

public static class KryptaLimits
{
    public const int MaxParticipantsPerRoom = 20;
    public const int MaxPendingPerRoom = 10;
    public const int JoinApprovalTimeoutSeconds = 120;
    public const int JoinMessageMaxLength = 200;

    public const int RoomIdLength = 6;
    public const string RoomIdPattern = "^[A-Z0-9]{6}$";
    public const int RoomNameMaxLength = 25;
    public const string DefaultRoomNamePrefix = "RM-";

    public const int NicknameMinLength = 3;
    public const int NicknameMaxLength = 25;

    public const int MessageMaxLength = 2_000;
    public const int MaxCiphertextBytes = 8_192;
    public const int MaxFrameBytes = 16_384;
    public const int SendQueueCapacity = 100;

    public const int RoomKeyBytes = 32;
    public const int PublicKeyBytes = 65;
    public const int NonceBytes = 12;
    public const int GcmTagBytes = 16;
    public const int WrappedKeyCiphertextBytes = RoomKeyBytes + GcmTagBytes;
    public const string KeyWrapInfo = "krypta-key-wrap-v1";
    public const int VerificationCodeDigits = 16;

    public const int RekeyMinIntervalSeconds = 30;
    public const int PreviousKeyWindowSeconds = 60;

    public const int RateLimitMessages = 10;
    public const int RateLimitMessagesWindowSeconds = 5;
    public const int RateLimitCommands = 3;
    public const int RateLimitCommandsWindowSeconds = 10;

    public const int JoinRateLimitAttempts = 5;
    public const int JoinRateLimitWindowSeconds = 180;
    public const int JoinRateLimitBlockSeconds = 120;

    public const int PingIntervalSeconds = 30;
    public const int ConnectionTimeoutSeconds = 60;
}
