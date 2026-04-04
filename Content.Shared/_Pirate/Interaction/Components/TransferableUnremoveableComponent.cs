using Content.Shared.Inventory;
using Content.Shared._Pirate.Interaction.EntitySystems;
using Robust.Shared.Timing;

namespace Content.Shared._Pirate.Interaction.Components;

[RegisterComponent]
[Access(typeof(TransferableUnremoveableSystem))]
public sealed partial class TransferableUnremoveableComponent : Component
{
    [DataField]
    public SlotFlags AllowedSlots = SlotFlags.BACK | SlotFlags.BELT;

    public GameTick AllowRemovalUntil = GameTick.Zero;
}
