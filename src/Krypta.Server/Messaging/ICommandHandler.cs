using Krypta.Server.Rooms;
using Krypta.Shared.Protocol;

namespace Krypta.Server.Messaging;

public interface ICommandHandler
{
    ValueTask HandleAsync(
        Participant participant,
        CommandClientMessage command,
        CancellationToken cancellationToken);
}
