using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Pirate.Weapons.Melee.Components;

[RegisterComponent]
public sealed partial class KatanaSheathComponent : Component
{
    [DataField("slot")]
    public string Slot = "item";
}
