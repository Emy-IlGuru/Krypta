namespace Krypta.Server.Connections;

public enum DisconnectReason
{
    None,
    SlowClient,
    HeartbeatTimeout,
    WriterFailure,
    ServerShutdown
}
