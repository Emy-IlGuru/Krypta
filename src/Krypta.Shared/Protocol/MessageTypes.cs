namespace Krypta.Shared.Protocol;

public static class MessageTypes
{
    public const string Join = "join";
    public const string Message = "message";
    public const string Command = "command";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Rekey = "rekey";
    public const string Cancel = "cancel";
    public const string Leave = "leave";
    public const string Pong = "pong";

    public const string Joined = "joined";
    public const string Participants = "participants";
    public const string Roster = "roster";
    public const string System = "system";
    public const string Clear = "clear";
    public const string Left = "left";
    public const string Ping = "ping";
    public const string Error = "error";
    public const string Pending = "pending";
    public const string Waiting = "waiting";
    public const string Rejected = "rejected";
    public const string Rekeyed = "rekeyed";
}
