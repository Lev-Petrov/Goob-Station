using Content.Goobstation.Maths.FixedPoint;
using Content.Goobstation.Shared.Sprinting;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Cloning.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Standing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Pirate.Shared.Traits.PhysicalPotential;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Pirate.Server.Traits.PhysicalPotential
{
    public sealed class PhysicalPotentialSystem : EntitySystem
    {
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly HungerSystem _hungerSystem = default!;
        [Dependency] private readonly IRobustRandom _random = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<MeleeHitEvent>(OnMeleeHit);
            SubscribeLocalEvent<PhysicalPotentialComponent, CloningEvent>(OnClone);
            SubscribeLocalEvent<PhysicalPotentialComponent, DamageModifyEvent>(OnDamageModify);
            SubscribeLocalEvent<PhysicalPotentialComponent, StoodEvent>(OnStood);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
            var query = EntityQueryEnumerator<PhysicalPotentialComponent>();
            while (query.MoveNext(out var uid, out var comp))
            {
                UpdateSprintProgress(frameTime, uid, comp);
                HandleRecovery(uid, comp);
            }
        }

        #region Calculate strains
        // -- HITS --
        private void OnMeleeHit(MeleeHitEvent args)
        {
            if (!TryComp<PhysicalPotentialComponent>(args.User, out var comp)
                || !TryComp<MeleeWeaponComponent>(args.Weapon, out var melee))
                return;

            foreach (var hitEntity in args.HitEntities)
            {
                if (!TryComp<MobStateComponent>(hitEntity, out var mob)) continue;
                if (mob.CurrentState != MobState.Alive) continue;

                var damageStrain = GetDamageStain(comp, melee);
                // Create and queue a new training strain
                var newStrain = new TrainingStrain { Damage = damageStrain };
                AddStrain(comp, newStrain);
            }

            // Apply current bonus to the final attack damage 
            args.BonusDamage += comp.DamageBonus;
        }

        public DamageSpecifier GetDamageStain(PhysicalPotentialComponent comp, MeleeWeaponComponent melee)
        {
            // Extract the physical damage profile from the weapon's damage dictionary.
            var blunt = (float)melee.Damage.DamageDict.GetValueOrDefault("Blunt", 0);
            var slash = (float)melee.Damage.DamageDict.GetValueOrDefault("Slash", 0);
            var piercing = (float)melee.Damage.DamageDict.GetValueOrDefault("Piercing", 0);
            var totalDamage = blunt + slash + piercing;

            var damageStrain = new DamageSpecifier();

            if (totalDamage > 0)
            {
                // Keep the trained bonus aligned with the weapon's actual physical damage split.
                if (blunt > 0)
                    damageStrain.DamageDict["Blunt"] = FixedPoint2.New(blunt / totalDamage);

                if (slash > 0)
                    damageStrain.DamageDict["Slash"] = FixedPoint2.New(slash / totalDamage);

                if (piercing > 0)
                    damageStrain.DamageDict["Piercing"] = FixedPoint2.New(piercing / totalDamage);
            }
            else
            {
                damageStrain.DamageDict["Blunt"] = FixedPoint2.New(0.01);
            }

            damageStrain *= comp.DamageRisingSpeed;
            return damageStrain;
        }

        private void OnClone(Entity<PhysicalPotentialComponent> ent, ref CloningEvent args)
        {
            if (!args.Settings.EventComponents.Contains(Factory.GetRegistration(ent.Comp.GetType()).Name))
                return;

            var clone = EnsureComp<PhysicalPotentialComponent>(args.CloneUid);
            clone.trainingEffectiveness = ent.Comp.trainingEffectiveness;
            clone.Strains = new List<TrainingStrain>(ent.Comp.Strains.Count);
            foreach (var strain in ent.Comp.Strains)
            {
                clone.Strains.Add(new TrainingStrain
                {
                    Damage = new DamageSpecifier(strain.Damage),
                    Defense = strain.Defense,
                    Stamina = strain.Stamina
                });
            }

            clone.DamageBonus = new DamageSpecifier(ent.Comp.DamageBonus);
            clone.MaxDamageBonus = ent.Comp.MaxDamageBonus;
            clone.DamageRisingSpeed = ent.Comp.DamageRisingSpeed;
            clone.DefenseRisingSpeed = ent.Comp.DefenseRisingSpeed;
            clone.DefenseBonus = ent.Comp.DefenseBonus;
            clone.MaxDefenseBonus = ent.Comp.MaxDefenseBonus;
            clone.StaminaRisingSpeed = ent.Comp.StaminaRisingSpeed;
            clone.MaxStamina = ent.Comp.MaxStamina;
            clone.StaminaBonus = ent.Comp.StaminaBonus;
            clone.SprintTimer = ent.Comp.SprintTimer;
            clone.SprintInterval = ent.Comp.SprintInterval;
            clone.PushUpsEfficiency = ent.Comp.PushUpsEfficiency;
            clone.TimeForRest = ent.Comp.TimeForRest;
            clone.EndRestTime = ent.Comp.EndRestTime;
            clone.IsResting = ent.Comp.IsResting;
            clone.NextStrainTime = ent.Comp.NextStrainTime;
            clone.MaxStrainsNumber = ent.Comp.MaxStrainsNumber;
            clone.StrainsApplyingDelay = ent.Comp.StrainsApplyingDelay;
            clone.HungerCost = ent.Comp.HungerCost;

            if (TryComp<StaminaComponent>(args.CloneUid, out var stamina))
            {
                stamina.CritThreshold += clone.StaminaBonus;
                Dirty(args.CloneUid, stamina);
            }

            Dirty(args.CloneUid, clone);
        }

        // -- DAMAGE --
        private void OnDamageModify(EntityUid uid, PhysicalPotentialComponent comp, DamageModifyEvent args)
        {
            if (args.Origin == null) return;

            var trainsDefense = false;

            //Reduces incoming damage
            if (args.Damage.DamageDict.TryGetValue("Blunt", out var blunt))
            {
                trainsDefense |= blunt > FixedPoint2.Zero;
                args.Damage.DamageDict["Blunt"] = FixedPoint2.Max(
                    blunt - comp.DefenseBonus,
                    FixedPoint2.Zero);
            }

            if (args.Damage.DamageDict.TryGetValue("Slash", out var slash))
            {
                trainsDefense |= slash > FixedPoint2.Zero;
                args.Damage.DamageDict["Slash"] = FixedPoint2.Max(
                    slash - comp.DefenseBonus,
                    FixedPoint2.Zero);
            }

            if (args.Damage.DamageDict.TryGetValue("Piercing", out var piercing))
            {
                trainsDefense |= piercing > FixedPoint2.Zero;
                args.Damage.DamageDict["Piercing"] = FixedPoint2.Max(
                    piercing - comp.DefenseBonus,
                    FixedPoint2.Zero);
            }

            if (trainsDefense)
            {
                var newStrain = new TrainingStrain { Defense = comp.DefenseRisingSpeed };
                AddStrain(comp, newStrain);
            }
        }

        // -- PUSH-UP --
        private void OnStood(EntityUid uid, PhysicalPotentialComponent comp, StoodEvent args)
        {
            if (!TryComp<MeleeWeaponComponent>(uid, out var melee)) return;

            var damageStrain = GetDamageStain(comp, melee);

            // Create and queue a new training strain 
            var newStrain = new TrainingStrain
            {
                Damage = damageStrain * comp.PushUpsEfficiency,
                Defense = comp.DefenseRisingSpeed * comp.PushUpsEfficiency,
                Stamina = comp.StaminaRisingSpeed * comp.PushUpsEfficiency
            };

            AddStrain(comp, newStrain);
        }

        // -- STAMINA AND SPRINT --
        private void UpdateSprintProgress(float frameTime, EntityUid uid, PhysicalPotentialComponent comp)
        {
            if (!TryComp<SprinterComponent>(uid, out var sprinter)) return;

            if (sprinter.IsSprinting)
            {
                comp.SprintTimer += frameTime;

                // Check if the sprint duration has exceeded the defined interval for a "tick"
                if (comp.SprintTimer > comp.SprintInterval)
                {
                    comp.SprintTimer = 0;

                    var newStrain = new TrainingStrain { Stamina = comp.StaminaRisingSpeed };
                    AddStrain(comp, newStrain);
                }
            }
        }

        #endregion

        #region Strain Handling 
        // Adds a new training point to the processing queue 
        public void AddStrain(PhysicalPotentialComponent comp, TrainingStrain strain)
        {
            if (comp.Strains.Count < comp.MaxStrainsNumber)
            {
                int fullExecutions = (int) MathF.Floor(comp.trainingEffectiveness);

                for (int i = 0; i < fullExecutions; i++)
                {
                    comp.Strains.Add(strain);
                }

                float remainder = comp.trainingEffectiveness - fullExecutions;
                if (_random.Prob(remainder))
                {
                    comp.Strains.Add(strain);
                }
            }

            // Set cooldown (rest period) before training absorption begins 
            comp.EndRestTime = _timing.CurTime + TimeSpan.FromSeconds(comp.TimeForRest);
            comp.IsResting = true;
        }

        private void HandleRecovery(EntityUid uid, PhysicalPotentialComponent comp)
        {
            if (!TryComp<MobStateComponent>(uid, out var mob) || mob.CurrentState != MobState.Alive) return;

            // Check if the rest period after the last activity has ended 
            if (comp.IsResting && comp.EndRestTime < _timing.CurTime)
            {
                comp.IsResting = false;
            }

            // Gradually process the strain queue if the player is resting 
            if (!comp.IsResting && comp.Strains.Count > 0)
            {
                // Introduce a delay between iterations for smooth bonus progression 
                if (comp.NextStrainTime < _timing.CurTime)
                {
                    ApplyStrain(uid, comp);
                    comp.NextStrainTime = _timing.CurTime + TimeSpan.FromSeconds(comp.StrainsApplyingDelay);
                }
            }
        }

        // Apply a specific strain point to the character's stats 
        private void ApplyStrain(EntityUid uid, PhysicalPotentialComponent comp)
        {
            if (comp.Strains.Count == 0) return;

            var strain = comp.Strains[comp.Strains.Count - 1];

            // Update damage bonus
            if (comp.DamageBonus.GetTotal() < comp.MaxDamageBonus)
            {
                comp.DamageBonus += strain.Damage;
            }

            // Update defense bonus
            if (comp.DefenseBonus < comp.MaxDefenseBonus)
            {
                comp.DefenseBonus += strain.Defense;
            }

            // Update stamina bonus
            if (TryComp<StaminaComponent>(uid, out var stamina))
            {
                var staminaIncrease = MathF.Min(strain.Stamina, comp.MaxStamina - stamina.CritThreshold);
                if (staminaIncrease > 0f)
                {
                    stamina.CritThreshold += staminaIncrease;
                    comp.StaminaBonus += staminaIncrease;
                    Dirty(uid, stamina);
                }
            }

            comp.Strains.RemoveAt(comp.Strains.Count -1);

            // Mark component as dirty to sync data with the client 
            Dirty(uid, comp);

            // Deduct hunger/calories for training 
            if (TryComp<HungerComponent>(uid, out var hunger))
            {
                _hungerSystem.ModifyHunger(uid, -comp.HungerCost, hunger);
            }
        }
        #endregion
    }
}
