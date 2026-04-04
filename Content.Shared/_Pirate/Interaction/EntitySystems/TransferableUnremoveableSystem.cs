using Content.Shared.Actions.Events;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared._Pirate.Interaction.Components;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Shared._Pirate.Interaction.EntitySystems;

public sealed class TransferableUnremoveableSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TransferableUnremoveableComponent, ContainerGettingRemovedAttemptEvent>(OnRemoveAttempt);
        SubscribeLocalEvent<TransferableUnremoveableComponent, BeingEquippedAttemptEvent>(OnBeingEquippedAttempt);
        SubscribeLocalEvent<TransferableUnremoveableComponent, BeingUnequippedAttemptEvent>(OnBeingUnequippedAttempt);
        SubscribeLocalEvent<TransferableUnremoveableComponent, GotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<TransferableUnremoveableComponent, GotEquippedHandEvent>(OnGotEquippedHand);
        SubscribeLocalEvent<TransferableUnremoveableComponent, DisarmAttemptEvent>(OnDisarmAttempt);
        SubscribeAllEvent<RequestMoveHandItemEvent>(OnRequestMoveHandItem, before: [typeof(SharedHandsSystem)]);
        SubscribeAllEvent<UseSlotNetworkMessage>(OnUseSlotNetworkMessage, before: [typeof(InventorySystem)]);
    }

    private void OnRemoveAttempt(Entity<TransferableUnremoveableComponent> ent, ref ContainerGettingRemovedAttemptEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if (ent.Comp.AllowRemovalUntil >= _timing.CurTick)
            return;

        args.Cancel();
    }

    private void OnBeingEquippedAttempt(Entity<TransferableUnremoveableComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (args.Equipee != args.EquipTarget)
        {
            args.Cancel();
            return;
        }

        if ((args.SlotFlags & ent.Comp.AllowedSlots) == SlotFlags.NONE)
        {
            args.Cancel();
            return;
        }

        ent.Comp.AllowRemovalUntil = _timing.CurTick + 1;
    }

    private void OnBeingUnequippedAttempt(Entity<TransferableUnremoveableComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        if ((args.SlotFlags & ent.Comp.AllowedSlots) == SlotFlags.NONE)
        {
            args.Cancel();
            return;
        }

        if (args.Unequipee != args.UnEquipTarget)
        {
            args.Cancel();
            return;
        }

        ent.Comp.AllowRemovalUntil = _timing.CurTick + 1;
    }

    private void OnGotEquipped(Entity<TransferableUnremoveableComponent> ent, ref GotEquippedEvent args)
    {
        ent.Comp.AllowRemovalUntil = GameTick.Zero;
    }

    private void OnGotEquippedHand(Entity<TransferableUnremoveableComponent> ent, ref GotEquippedHandEvent args)
    {
        ent.Comp.AllowRemovalUntil = GameTick.Zero;
    }

    private void OnDisarmAttempt(Entity<TransferableUnremoveableComponent> ent, ref DisarmAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnRequestMoveHandItem(RequestMoveHandItemEvent args, EntitySessionEventArgs ev)
    {
        if (ev.SenderSession.AttachedEntity is not { } user)
            return;

        if (!_hands.TryGetHeldItem(user, args.HandName, out var item))
            return;

        if (!TryComp<TransferableUnremoveableComponent>(item, out var comp))
            return;

        comp.AllowRemovalUntil = _timing.CurTick + 1;
    }

    private void OnUseSlotNetworkMessage(UseSlotNetworkMessage args, EntitySessionEventArgs ev)
    {
        if (ev.SenderSession.AttachedEntity is not { } user)
            return;

        if (!TryComp<InventoryComponent>(user, out var inventory) ||
            !_inventory.TryGetSlot(user, args.Slot, out var slotDefinition, inventory))
        {
            return;
        }

        if (TryComp<HandsComponent>(user, out var hands) &&
            hands.ActiveHandId is { } activeHand &&
            _hands.TryGetHeldItem(user, activeHand, out var heldItem) &&
            TryComp<TransferableUnremoveableComponent>(heldItem, out var heldComp) &&
            (slotDefinition.SlotFlags & heldComp.AllowedSlots) != SlotFlags.NONE)
        {
            heldComp.AllowRemovalUntil = _timing.CurTick + 1;
            return;
        }

        if (_inventory.TryGetSlotEntity(user, args.Slot, out var slottedItem, inventory) &&
            TryComp<TransferableUnremoveableComponent>(slottedItem, out var slottedComp) &&
            (slotDefinition.SlotFlags & slottedComp.AllowedSlots) != SlotFlags.NONE)
        {
            slottedComp.AllowRemovalUntil = _timing.CurTick + 1;
        }
    }
}
