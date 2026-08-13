namespace Olympus.Services.AutoAttack;

/// <summary>
/// Allows <see cref="Rotation.RotationFactory"/> to attach auto-attack hold after construction.
/// </summary>
internal interface IAutoAttackHoldIntegration
{
    void AttachAutoAttackService(IAutoAttackService? service);
}
