using System.Numerics;
using Content.Shared._Goobstation.Wizard.Projectiles;
using Content.Shared._Pirate.Projectiles;
using Content.Shared._Pirate.Weapons.Melee.Components;
using Content.Shared._Pirate.Weapons.Ranged.Events;
using Content.Shared._Shitmed.ItemSwitch;
using Content.Shared._Shitmed.ItemSwitch.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Item;
using Content.Shared._White.Animations;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.Goobstation.Common.Effects;
using Content.Goobstation.Maths.FixedPoint;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Pirate.Weapons.Melee;

public sealed class TimedDeflectBlockSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ClothingSystem _clothing = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedItemSwitchSystem _itemSwitch = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SparksSystem _sparks = default!;
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TimedDeflectBlockComponent, ItemWieldedEvent>(OnWielded);
        SubscribeLocalEvent<TimedDeflectBlockComponent, ItemUnwieldedEvent>(OnUnwielded);
        SubscribeLocalEvent<TimedDeflectBlockComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<TimedDeflectBlockComponent, AfterAutoHandleStateEvent>(OnAfterState);
        SubscribeLocalEvent<TimedDeflectBlockComponent, ItemSwitchedEvent>(OnItemSwitched);
        SubscribeLocalEvent<TimedDeflectBlockComponent, HeldRelayedEvent<ProjectileReflectAttemptEvent>>(OnProjectileReflectAttempt);
        SubscribeLocalEvent<TimedDeflectBlockComponent, HeldRelayedEvent<HitScanReflectAttemptEvent>>(OnHitscanReflectAttempt);
        SubscribeLocalEvent<TimedDeflectBlockComponent, HeldRelayedEvent<HitScanBlockAttemptEvent>>(OnHitscanBlockAttempt);
        SubscribeLocalEvent<TimedDeflectBlockComponent, AttemptMeleeEvent>(OnAttemptMelee);
        SubscribeLocalEvent<TimedDeflectBlockComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<TimedDeflectBlockComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);

        SubscribeLocalEvent<HandsComponent, BeforeHarmfulActionEvent>(OnBeforeHarmfulAction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<TimedDeflectBlockComponent>();
        while (query.MoveNext(out var uid, out var block))
        {
            if (_timing.CurTime - block.LastDeflectTime < TimeSpan.FromSeconds(block.PowerDecayDelay) ||
                block.CurrentPower <= block.MinPower)
            {
                continue;
            }

            SetPower(uid, block, block.CurrentPower - block.PowerDecayPerSecond * frameTime);
        }
    }

    private void OnStartup(Entity<TimedDeflectBlockComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.CurrentPower = Math.Clamp(ent.Comp.CurrentPower, ent.Comp.MinPower, ent.Comp.MaxPower);
        ent.Comp.LastDeflectTime = _timing.CurTime;

        if (_net.IsServer)
            UpdateVisualState(ent.Owner, ent.Comp);
    }

    private void OnWielded(Entity<TimedDeflectBlockComponent> ent, ref ItemWieldedEvent args)
    {
        if (_net.IsServer)
            ent.Comp.DeflectWindowStart = _timing.CurTime - GetActivationGrace(args.User, ent.Comp);

        ent.Comp.DeflectWindowEnd = _timing.CurTime + TimeSpan.FromSeconds(GetDeflectWindow(ent.Comp));

        if (_net.IsServer)
            Dirty(ent.Owner, ent.Comp);
    }

    private void OnAfterState(Entity<TimedDeflectBlockComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplyReplicatedVisualState(ent.Owner, GetVisualState(ent.Comp));
    }

    private void OnItemSwitched(Entity<TimedDeflectBlockComponent> ent, ref ItemSwitchedEvent args)
    {
        ApplyReplicatedVisualState(ent.Owner, args.State);
    }

    private void OnUnwielded(Entity<TimedDeflectBlockComponent> ent, ref ItemUnwieldedEvent args)
    {
        ent.Comp.DeflectWindowStart = TimeSpan.Zero;
        ent.Comp.DeflectWindowEnd = TimeSpan.Zero;

        if (_net.IsServer)
            Dirty(ent.Owner, ent.Comp);
    }

    private void OnBeforeHarmfulAction(Entity<HandsComponent> ent, ref BeforeHarmfulActionEvent args)
    {
        if (args.Cancelled || args.Type != HarmfulActionType.Harm)
            return;

        if (!TryGetActiveDeflectWeapon(ent, out var weapon, out var block))
            return;

        ApplyDefense(ent.Owner, weapon, block, args.User, projectile: null, out var deflected);
        args.Cancel();
    }

    private void OnProjectileReflectAttempt(
        Entity<TimedDeflectBlockComponent> ent,
        ref HeldRelayedEvent<ProjectileReflectAttemptEvent> args)
    {
        if (args.Args.Cancelled ||
            !TryComp<WieldableComponent>(ent, out var wieldable) ||
            !wieldable.Wielded)
        {
            return;
        }

        if (ent.Comp.DeflectToSource &&
            args.Args.Component.Shooter is { } shooter &&
            TryDeflectProjectileToSource(args.Args.Target, ent.Owner, ent.Comp, shooter, args.Args.ProjUid, args.Args.Component))
        {
            args.Args.Cancelled = true;
            return;
        }

        ApplyDefense(args.Args.Target, ent.Owner, ent.Comp, args.Args.Component.Shooter, args.Args.ProjUid, out _);
        args.Args.Cancelled = true;
    }

    private void OnHitscanReflectAttempt(
        Entity<TimedDeflectBlockComponent> ent,
        ref HeldRelayedEvent<HitScanReflectAttemptEvent> args)
    {
        if (args.Args.Reflected ||
            !ent.Comp.DeflectToSource ||
            args.Args.Shooter == null ||
            !TryComp<WieldableComponent>(ent, out var wieldable) ||
            !wieldable.Wielded ||
            !TryApplyDirectedDeflect(args.Args.Target, ent.Owner, ent.Comp, args.Args.Shooter.Value))
        {
            return;
        }

        var direction = GetDirectionToEntity(args.Args.Target, args.Args.Shooter.Value);
        args.Args.Direction = direction == Vector2.Zero
            ? -args.Args.Direction
            : direction;
        args.Args.Reflected = true;
    }

    private void OnHitscanBlockAttempt(
        Entity<TimedDeflectBlockComponent> ent,
        ref HeldRelayedEvent<HitScanBlockAttemptEvent> args)
    {
        if (args.Args.Cancelled ||
            !TryComp<WieldableComponent>(ent, out var wieldable) ||
            !wieldable.Wielded)
        {
            return;
        }

        ApplyDefense(args.Args.Target, ent.Owner, ent.Comp, args.Args.Shooter, projectile: null, out _);
        args.Args.Cancelled = true;
    }

    private void OnAttemptMelee(Entity<TimedDeflectBlockComponent> ent, ref AttemptMeleeEvent args)
    {
        if (!TryComp<WieldableComponent>(ent, out var wieldable) || !wieldable.Wielded)
            return;

        args.Cancelled = true;
    }

    private void OnGetMeleeDamage(Entity<TimedDeflectBlockComponent> ent, ref GetMeleeDamageEvent args)
    {
        ConvertSlashDamageToBonusType(ent.Comp, args.Damage);

        var bonusDamage = GetLevel(ent.Comp) * ent.Comp.DamageBonusPerLevel;
        if (bonusDamage <= 0f)
            return;

        var bonus = new DamageSpecifier();
        bonus.DamageDict[ent.Comp.BonusDamageType] = FixedPoint2.New(bonusDamage);
        args.Damage += bonus;
    }

    private void ConvertSlashDamageToBonusType(TimedDeflectBlockComponent block, DamageSpecifier damage)
    {
        if (block.BonusDamageType == "Slash" ||
            !damage.DamageDict.Remove("Slash", out var slashDamage) ||
            slashDamage <= FixedPoint2.Zero)
        {
            return;
        }

        if (damage.DamageDict.TryGetValue(block.BonusDamageType, out var existingDamage))
            damage.DamageDict[block.BonusDamageType] = existingDamage + slashDamage;
        else
            damage.DamageDict[block.BonusDamageType] = slashDamage;
    }

    private void OnMeleeHit(Entity<TimedDeflectBlockComponent> ent, ref MeleeHitEvent args)
    {
        if (!_net.IsServer)
            return;

        SetPower(ent.Owner, ent.Comp, ent.Comp.CurrentPower - 5f);
    }

    private bool TryGetActiveDeflectWeapon(
        Entity<HandsComponent> user,
        out EntityUid weapon,
        out TimedDeflectBlockComponent block)
    {
        EntityUid bestWeapon = EntityUid.Invalid;
        TimedDeflectBlockComponent? bestBlock = null;

        foreach (var held in _hands.EnumerateHeld((user.Owner, user.Comp)))
        {
            if (!TryComp<TimedDeflectBlockComponent>(held, out TimedDeflectBlockComponent? foundBlock) ||
                !TryComp<WieldableComponent>(held, out var wieldable) ||
                !wieldable.Wielded)
            {
                continue;
            }

            if (bestBlock == null)
            {
                bestWeapon = held;
                bestBlock = foundBlock;
                continue;
            }

            var foundInWindow = IsWithinDeflectWindow(user.Owner, foundBlock);
            var bestInWindow = IsWithinDeflectWindow(user.Owner, bestBlock);

            if (foundInWindow && !bestInWindow ||
                foundInWindow == bestInWindow && foundBlock.CurrentPower > bestBlock.CurrentPower)
            {
                bestWeapon = held;
                bestBlock = foundBlock;
            }
        }

        weapon = bestWeapon;
        block = bestBlock!;
        return bestBlock != null;
    }

    private bool TryApplyDirectedDeflect(
        EntityUid defender,
        EntityUid weapon,
        TimedDeflectBlockComponent block,
        EntityUid attacker)
    {
        if (!IsWithinDeflectWindow(defender, block))
            return false;

        ApplySuccessfulDeflect(defender, weapon, block, attacker);
        return true;
    }

    private bool TryDeflectProjectileToSource(
        EntityUid defender,
        EntityUid weapon,
        TimedDeflectBlockComponent block,
        EntityUid attacker,
        EntityUid projectileUid,
        ProjectileComponent projectile)
    {
        if (!TryApplyDirectedDeflect(defender, weapon, block, attacker) ||
            !TryComp<PhysicsComponent>(projectileUid, out var physics))
        {
            return false;
        }

        var direction = GetDirectionToEntity(projectileUid, attacker);
        if (direction == Vector2.Zero)
            return false;

        var existingVelocity = _physics.GetMapLinearVelocity(projectileUid, physics);
        var speed = existingVelocity.Length();
        if (speed <= 0.001f)
            speed = physics.LinearVelocity.Length();

        if (speed <= 0.001f)
            return false;

        var desiredVelocity = direction * speed;
        _physics.SetLinearVelocity(projectileUid, physics.LinearVelocity + desiredVelocity - existingVelocity, body: physics);
        _transform.SetWorldRotation(projectileUid, direction.ToWorldAngle() + projectile.Angle);

        projectile.Shooter = defender;
        projectile.Weapon = defender;
        projectile.ProjectileSpent = false;
        projectile.IgnoredEntities.Clear();

        if (TryGetMapPosition(attacker, out var attackerPosition))
            projectile.TargetCoordinates = attackerPosition.Position;

        if (TryComp<HomingProjectileComponent>(projectileUid, out var homing))
        {
            homing.Target = attacker;

            if (_net.IsServer)
                Dirty(projectileUid, homing);
        }

        if (_net.IsServer)
            Dirty(projectileUid, projectile);

        return true;
    }

    private void ApplyDefense(
        EntityUid defender,
        EntityUid weapon,
        TimedDeflectBlockComponent block,
        EntityUid? attacker,
        EntityUid? projectile,
        out bool deflected)
    {
        deflected = IsWithinDeflectWindow(defender, block);

        if (deflected)
            ApplySuccessfulDeflect(defender, weapon, block, attacker);
        else
        {
            AdjustStamina(defender, GetBlockStaminaDamage(block), attacker, weapon);
            _audio.PlayPredicted(block.BlockSound, weapon, attacker);
        }

        if (projectile is { } projectileUid && !Deleted(projectileUid))
        {
            var deleteEv = new DeletingProjectileEvent(projectileUid);
            RaiseLocalEvent(ref deleteEv);
            PredictedQueueDel(projectileUid);
        }
    }

    private void ApplySuccessfulDeflect(
        EntityUid defender,
        EntityUid weapon,
        TimedDeflectBlockComponent block,
        EntityUid? attacker)
    {
        AddDeflectPower(weapon, block);
        block.DeflectWindowEnd = _timing.CurTime + TimeSpan.FromSeconds(GetDeflectWindow(block));

        if (_net.IsServer)
            Dirty(weapon, block);

        AdjustStamina(defender, GetDeflectStaminaCost(block), attacker, weapon);
        _sparks.DoSparks(Transform(weapon).Coordinates, minSparks: 1, maxSparks: 2, playSound: false);
        _audio.PlayPredicted(block.DeflectSound, weapon, attacker);
        TryBackflip(defender, block);
    }

    private void AdjustStamina(EntityUid defender, float fraction, EntityUid? attacker, EntityUid weapon)
    {
        if (!_net.IsServer ||
            fraction == 0f ||
            !TryComp<StaminaComponent>(defender, out var stamina))
        {
            return;
        }

        var amount = stamina.CritThreshold * fraction;
        _stamina.TakeStaminaDamage(defender, amount, stamina, source: attacker, with: weapon, visual: false, logDamage: amount > 0f);
    }

    private void AddDeflectPower(EntityUid weapon, TimedDeflectBlockComponent block)
    {
        block.LastDeflectTime = _timing.CurTime;
        SetPower(weapon, block, block.CurrentPower + block.PowerGainOnDeflect);
    }

    private void SetPower(EntityUid weapon, TimedDeflectBlockComponent block, float power)
    {
        var clampedPower = Math.Clamp(power, block.MinPower, block.MaxPower);
        if (Math.Abs(block.CurrentPower - clampedPower) < 0.001f)
            return;

        block.CurrentPower = clampedPower;

        if (_net.IsServer)
        {
            UpdateVisualState(weapon, block);
            Dirty(weapon, block);
        }
    }

    private void UpdateVisualState(EntityUid weapon, TimedDeflectBlockComponent block)
    {
        if (!TryComp<ItemSwitchComponent>(weapon, out var itemSwitch))
            return;

        var state = GetVisualState(block);
        var wieldedState = GetWieldedInhandState(state);

        if (itemSwitch.State != state)
        {
            _itemSwitch.Switch((weapon, itemSwitch), state, predicted: false);
        }

        if (!TryComp<WieldableComponent>(weapon, out var wieldable))
            return;

        wieldable.WieldedInhandPrefix = wieldedState;
        wieldable.OldInhandPrefix = state;

        if (TryComp<ItemComponent>(weapon, out var item) && wieldable.Wielded)
            _item.SetHeldPrefix(weapon, wieldedState, component: item);

        Dirty(weapon, wieldable);
    }

    private void ApplyReplicatedVisualState(EntityUid weapon, string state)
    {
        if (!HasComp<ItemSwitchComponent>(weapon))
            return;

        var wieldedState = GetWieldedInhandState(state);
        var changed = false;

        if (TryComp<ItemComponent>(weapon, out var item))
        {
            var targetHeldPrefix = TryComp<WieldableComponent>(weapon, out var wieldableState) && wieldableState.Wielded
                ? wieldedState
                : state;

            if (item.HeldPrefix != targetHeldPrefix)
            {
                _item.SetHeldPrefix(weapon, targetHeldPrefix, component: item);
                changed = true;
            }
        }

        if (TryComp<ClothingComponent>(weapon, out var clothing) && clothing.EquippedPrefix != state)
        {
            _clothing.SetEquippedPrefix(weapon, state, clothing);
            changed = true;
        }

        if (TryComp<WieldableComponent>(weapon, out var wieldable))
        {
            if (wieldable.WieldedInhandPrefix != wieldedState)
            {
                wieldable.WieldedInhandPrefix = wieldedState;
                changed = true;
            }

            if (wieldable.OldInhandPrefix != state)
            {
                wieldable.OldInhandPrefix = state;
                changed = true;
            }
        }

        if (changed)
            _item.VisualsChanged(weapon);
    }

    private float GetBlockStaminaDamage(TimedDeflectBlockComponent block)
    {
        return GetBaseStaminaDamage(block) * 1.5f;
    }

    private float GetDeflectStaminaCost(TimedDeflectBlockComponent block)
    {
        return GetBaseStaminaDamage(block);
    }

    private float GetBaseStaminaDamage(TimedDeflectBlockComponent block)
    {
        return MathF.Max(0f, block.BlockStaminaDamageFraction - GetLevel(block) * block.BlockStaminaDamageReductionPerLevel);
    }

    private float GetDeflectWindow(TimedDeflectBlockComponent block)
    {
        return block.DeflectWindow + GetLevel(block) * block.DeflectWindowBonusPerLevel;
    }

    private bool IsWithinDeflectWindow(EntityUid defender, TimedDeflectBlockComponent block)
    {
        var now = _timing.CurTime;
        var lagComp = GetDeflectLagCompensation(defender, block);
        return now >= block.DeflectWindowStart && now <= block.DeflectWindowEnd + lagComp;
    }

    private TimeSpan GetDeflectLagCompensation(EntityUid defender, TimedDeflectBlockComponent block)
    {
        if (block.DeflectLagCompensationMultiplier <= 0f ||
            block.MaxDeflectLagCompensation <= 0f ||
            !_player.TryGetSessionByEntity(defender, out var session))
        {
            return TimeSpan.Zero;
        }

        var compensationSeconds = MathF.Min(
            block.MaxDeflectLagCompensation,
            session.Ping / 2000f * block.DeflectLagCompensationMultiplier);

        return TimeSpan.FromSeconds(compensationSeconds);
    }

    private TimeSpan GetActivationGrace(EntityUid? holder, TimedDeflectBlockComponent block)
    {
        if (!_net.IsServer ||
            block.BlockActivationLagCompensationMultiplier <= 0f ||
            block.MaxBlockActivationLagCompensation <= 0f ||
            holder == null ||
            !_player.TryGetSessionByEntity(holder.Value, out var session))
        {
            return TimeSpan.Zero;
        }

        var graceSeconds = MathF.Min(
            block.MaxBlockActivationLagCompensation,
            session.Ping / 1000f * block.BlockActivationLagCompensationMultiplier);

        return TimeSpan.FromSeconds(graceSeconds);
    }

    private Vector2 GetDirectionToEntity(EntityUid from, EntityUid to)
    {
        if (!TryGetMapPosition(from, out var fromPosition) ||
            !TryGetMapPosition(to, out var toPosition))
        {
            return Vector2.Zero;
        }

        var direction = toPosition.Position - fromPosition.Position;
        return direction == Vector2.Zero
            ? Vector2.Zero
            : direction.Normalized();
    }

    private bool TryGetMapPosition(EntityUid entity, out MapCoordinates coordinates)
    {
        coordinates = default;

        if (TerminatingOrDeleted(entity) ||
            !TryComp<TransformComponent>(entity, out var xform))
        {
            return false;
        }

        coordinates = _transform.GetMapCoordinates((entity, xform));
        return true;
    }

    private void TryBackflip(EntityUid defender, TimedDeflectBlockComponent block)
    {
        if (!_net.IsServer ||
            block.BackflipChance <= 0f ||
            !_random.Prob(Math.Clamp(block.BackflipChance, 0f, 1f)))
        {
            return;
        }

        RaiseNetworkEvent(new FlipOnHitEvent(GetNetEntity(defender)), Filter.Pvs(defender, entityManager: EntityManager));
    }

    private int GetLevel(TimedDeflectBlockComponent block)
    {
        if (block.PowerPerLevel <= 0f)
            return 0;

        return Math.Clamp((int) (block.CurrentPower / block.PowerPerLevel), 0, block.MaxLevel);
    }

    private string GetVisualState(TimedDeflectBlockComponent block)
    {
        var level = GetLevel(block);
        return level <= 0
            ? block.BaseVisualState
            : $"{block.LevelVisualStatePrefix}{level}";
    }

    private string GetWieldedInhandState(string state)
    {
        return $"{state}-wielded";
    }
}
