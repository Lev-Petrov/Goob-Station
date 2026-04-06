using Content.Shared.Containers.ItemSlots;
using Content.Shared._Pirate.Weapons.Melee.Components;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Shared._Pirate.Weapons.Melee;

public sealed class KatanaSheathSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KatanaSheathComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<KatanaSheathComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<KatanaSheathComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
    }

    private void OnMapInit(Entity<KatanaSheathComponent> ent, ref MapInitEvent args)
    {
        UpdateAppearance(ent);
    }

    private void OnItemInserted(Entity<KatanaSheathComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        UpdateAppearance(ent);
    }

    private void OnItemRemoved(Entity<KatanaSheathComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        UpdateAppearance(ent);
    }

    private void UpdateAppearance(Entity<KatanaSheathComponent> ent)
    {
        if (!_itemSlots.TryGetSlot(ent, ent.Comp.Slot, out var slot) ||
            slot.Item is not { } stored ||
            !TryComp<KatanaSheathHandleComponent>(stored, out var handle))
        {
            ClearAppearance(ent);
            return;
        }

        _appearance.SetData(ent, KatanaSheathVisuals.InventoryHandle, CreateLayer(handle.Sprite, handle.InventoryState));
        _appearance.SetData(ent, KatanaSheathVisuals.BeltHandle, CreateLayer(handle.Sprite, handle.BeltState));
        _appearance.SetData(ent, KatanaSheathVisuals.BackpackHandle, CreateLayer(handle.Sprite, handle.BackpackState));
    }

    private void ClearAppearance(Entity<KatanaSheathComponent> ent)
    {
        _appearance.RemoveData(ent, KatanaSheathVisuals.InventoryHandle);
        _appearance.RemoveData(ent, KatanaSheathVisuals.BeltHandle);
        _appearance.RemoveData(ent, KatanaSheathVisuals.BackpackHandle);
    }

    private static PrototypeLayerData CreateLayer(ResPath sprite, string state)
    {
        return new PrototypeLayerData
        {
            RsiPath = sprite.ToString(),
            State = state,
        };
    }
}
