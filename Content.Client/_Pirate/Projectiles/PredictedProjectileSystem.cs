using Content.Shared.Projectiles;
using Content.Shared._Pirate.Projectiles;
using Robust.Client.GameObjects;

namespace Content.Client._Pirate.Projectiles;

public sealed class PredictedProjectileSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProjectileComponent, Robust.Client.Physics.UpdateIsPredictedEvent>(OnUpdateIsPredicted);
        SubscribeLocalEvent<DeletingProjectileEvent>(OnDeletingProjectile);
        SubscribeNetworkEvent<ShotPredictedProjectileEvent>(OnShotPredictedProjectile);
    }

    private void OnUpdateIsPredicted(Entity<ProjectileComponent> ent, ref Robust.Client.Physics.UpdateIsPredictedEvent args)
    {
        args.IsPredicted = true;
    }

    private void OnDeletingProjectile(ref DeletingProjectileEvent args)
    {
        RemComp<SpriteComponent>(args.Entity);
        RemComp<PointLightComponent>(args.Entity);
    }

    private void OnShotPredictedProjectile(ShotPredictedProjectileEvent args)
    {
        var uid = GetEntity(args.Projectile);
        if (!uid.IsValid())
            return;

        HideAuthoritativeVisuals(uid);
    }

    private void HideAuthoritativeVisuals(EntityUid uid)
    {
        if (!HasComp<ProjectileComponent>(uid))
            return;

        if (TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.SetVisible((uid, sprite), false);

        if (TryComp<PointLightComponent>(uid, out var light))
            _lights.SetEnabled(uid, false, light);
    }
}
