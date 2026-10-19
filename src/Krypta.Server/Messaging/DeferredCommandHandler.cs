using Krypta.Server.Rooms;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Messaging;

/// <summary>
/// Segnaposto sostituito dal CommandHandler nel punto 1A.3.
/// </summary>
public sealed class DeferredCommandHandler(ProtocolMessageSender sender) : ICommandHandler
{
    public ValueTask HandleAsync(
        Participant participant,
        CommandClientMessage command,
        CancellationToken cancellationToken)
    {
        sender.SendError(participant, ErrorCodes.UnknownCommand);
        return ValueTask.CompletedTask;
    }
}
