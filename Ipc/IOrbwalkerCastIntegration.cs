namespace Olympus.Ipc;

/// <summary>
/// Allows <see cref="Rotation.RotationFactory"/> to attach Orbwalker IPC after construction
/// without threading the dependency through every concrete rotation constructor.
/// </summary>
internal interface IOrbwalkerCastIntegration
{
    void AttachOrbwalkerIpc(IOrbwalkerIpc? ipc);
}
