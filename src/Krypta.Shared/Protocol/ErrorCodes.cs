namespace Krypta.Shared.Protocol;

public static class ErrorCodes
{
    public const string InvalidRoomId = "INVALID_ROOM_ID";
    public const string InvalidNickname = "INVALID_NICKNAME";
    public const string RoomFull = "ROOM_FULL";
    public const string AlreadyInRoom = "ALREADY_IN_ROOM";
    public const string AlreadyPending = "ALREADY_PENDING";
    public const string WaitingRoomFull = "WAITING_ROOM_FULL";
    public const string UnknownRequest = "UNKNOWN_REQUEST";
    public const string NotPending = "NOT_PENDING";
    public const string NotInRoom = "NOT_IN_ROOM";
    public const string MessageTooLong = "MESSAGE_TOO_LONG";
    public const string InvalidEnvelope = "INVALID_ENVELOPE";
    public const string InvalidPublicKey = "INVALID_PUBLIC_KEY";
    public const string InvalidJoinMessage = "INVALID_JOIN_MESSAGE";
    public const string InvalidApproval = "INVALID_APPROVAL";
    public const string InvalidRekey = "INVALID_REKEY";
    public const string StaleKeyEpoch = "STALE_KEY_EPOCH";
    public const string RekeyRateLimited = "REKEY_RATE_LIMITED";
    public const string RateLimited = "RATE_LIMITED";
    public const string JoinRateLimited = "JOIN_RATE_LIMITED";
    public const string UnknownCommand = "UNKNOWN_COMMAND";
    public const string InvalidCommandArg = "INVALID_COMMAND_ARG";
    public const string CommandRateLimited = "COMMAND_RATE_LIMITED";
    public const string Malformed = "MALFORMED";
    public const string UnknownType = "UNKNOWN_TYPE";
    public const string ServerShutdown = "SERVER_SHUTDOWN";
}
