using Olympus.Services.Movement;

namespace Olympus.Rotation.Common.Helpers;

/// <summary>
/// Allows <see cref="Rotation.RotationFactory"/> to attach BossMod presence after construction.
/// </summary>
internal interface IBossModPresenceIntegration
{
    void AttachBossModPresence(IBossModPresence? presence);
}
