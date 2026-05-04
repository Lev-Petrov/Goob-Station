using Content.Goobstation.Maths.FixedPoint;
using Content.Goobstation.Shared.Sprinting;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Cloning.Events;
using Content.Shared.Movement.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Pirate.Shared.Traits.PhysicalPotential;
using Robust.Shared.Random;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Content.Shared.Alert;
using Content.Shared.Examine;
using Content.Shared.Standing;
using Robust.Shared.Enums;
using Content.Shared.Humanoid;
using Robust.Shared.GameObjects.Components.Localization;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Popups;
using static Content.Shared.Administration.Notes.AdminMessageEuiState;

namespace Content.Pirate.Server.Traits.PhysicalPotential
{
    public sealed class PhysicalPotentialSystem : EntitySystem
    {
        private static readonly string[] PhysicalDamageTypes = { "Blunt", "Slash", "Piercing" };

        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly AlertsSystem _alertsSystem = default!;
        [Dependency] private readonly SharedPopupSystem _popup = default!;


        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<MeleeHitEvent>(OnMeleeHit);
            SubscribeLocalEvent<PhysicalPotentialComponent, DamageModifyEvent>(OnDamageModify);

            SubscribeLocalEvent<PhysicalPotentialComponent, StoodEvent>(OnStood);
            SubscribeLocalEvent<PhysicalPotentialComponent, DownedEvent>(OnDowned);

            SubscribeLocalEvent<SolutionComponent, SolutionChangedEvent>(OnSolutionChanged);

            SubscribeLocalEvent<PhysicalPotentialComponent, ComponentInit>(OnComponentInit);

            SubscribeLocalEvent<PhysicalPotentialComponent, CloningEvent>(OnClone);

            SubscribeLocalEvent<PhysicalPotentialComponent, ExaminedEvent>(OnExamine);
        }

        private void OnComponentInit(EntityUid uid, PhysicalPotentialComponent comp, ComponentInit args)
        {
            UpdateAlert(uid, comp);
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

        #region Technical strains
        // -- HITS --
        private void OnMeleeHit(MeleeHitEvent args)
        {
            if (!TryComp<PhysicalPotentialComponent>(args.User, out var comp))
                return;

            args.BonusDamage += comp.DamageBonus * comp.MuscleMass;

            var resolvedDamage = new DamageSpecifier(args.BaseDamage);
            resolvedDamage += args.BonusDamage;
            resolvedDamage = DamageSpecifier.ApplyModifierSets(resolvedDamage, args.ModifiersList);

            var damageStrain = GetDamageStain(comp, resolvedDamage);
            if (damageStrain.Empty)
                return;

            foreach (var hitEntity in args.HitEntities)
            {
                if (!TryComp<MobStateComponent>(hitEntity, out var mob)) continue;
                if (mob.CurrentState != MobState.Alive) continue;

                // Create and queue a new training strain
                var newStrain = new TechnicalStrain { Damage = damageStrain };
                AddTechnicalStrain(comp, newStrain);
            }
        }

        public DamageSpecifier GetDamageStain(PhysicalPotentialComponent comp, DamageSpecifier damage)
        {
            var damageStrain = new DamageSpecifier();
            var totalDamage = FixedPoint2.Zero;

            foreach (var type in PhysicalDamageTypes)
            {
                if (damage.DamageDict.TryGetValue(type, out var amount) && amount > FixedPoint2.Zero)
                    totalDamage += amount;
            }

            if (totalDamage <= FixedPoint2.Zero)
                return damageStrain;

            foreach (var type in PhysicalDamageTypes)
            {
                if (damage.DamageDict.TryGetValue(type, out var amount) && amount > FixedPoint2.Zero)
                    damageStrain.DamageDict[type] = amount / totalDamage;
            }

            damageStrain *= comp.DamageRisingSpeed;
            return damageStrain;
        }

        // --DEFENSE--
        private void OnDamageModify(EntityUid uid, PhysicalPotentialComponent comp, DamageModifyEvent args)
        {
            // Try to reduce incoming physical damage by the entity's defense bonus.
            var trainsDefense = ApplyDefenseReduction(args.Damage, comp.DefenseBonus * comp.MuscleMass);

            // Check if the damaged entity is still alive.
            var isAlive = TryComp<MobStateComponent>(uid, out var mob) && mob.CurrentState == MobState.Alive;

            // Add strain
            if (args.Origin != null && trainsDefense && isAlive && args.Damage.GetTotal() > 0)
            {
                var newStrain = new TechnicalStrain
                {
                    // Defense strain gained per successful physical hit.
                    Defense = comp.DefenseRisingSpeed
                };

                AddTechnicalStrain(comp, newStrain);
            }
        }

        private static bool ApplyDefenseReduction(DamageSpecifier damage, FixedPoint2 defenseBonus)
        {
            var totalPhysicalDamage = FixedPoint2.Zero;

            // Stores the last valid physical damage type.
            // This is used to dump all rounding leftovers into the final type
            // so that the full defenseBonus is consumed accurately.
            string? lastPositiveType = null;

            // First pass: calculate total physical damage only.
            foreach (var type in PhysicalDamageTypes)
            {
                if (!damage.DamageDict.TryGetValue(type, out var amount) || amount <= FixedPoint2.Zero)
                    continue;

                totalPhysicalDamage += amount;
                lastPositiveType = type;
            }

            if (totalPhysicalDamage <= FixedPoint2.Zero)
                return false;

            // Defense cannot reduce more than total available physical damage.
            var remainingReduction = FixedPoint2.Min(defenseBonus, totalPhysicalDamage);

            // If defense bonus is zero or something went wrong, exit.
            if (remainingReduction <= FixedPoint2.Zero || lastPositiveType == null)
                return true;

            // Second pass: distribute defense reduction proportionally
            // across all physical damage types.
            foreach (var type in PhysicalDamageTypes)
            {
                if (!damage.DamageDict.TryGetValue(type, out var amount) || amount <= FixedPoint2.Zero)
                    continue;

                FixedPoint2 reduction;

                if (type == lastPositiveType)
                {
                    // The final damage type receives all remaining reduction.
                    reduction = FixedPoint2.Min(amount, remainingReduction);
                }
                else
                {
                    // Apply proportional reduction based on this type's share
                    // of the current total physical damage.
                    reduction = FixedPoint2.Min(amount, totalPhysicalDamage == FixedPoint2.Zero? FixedPoint2.Zero: remainingReduction * amount / totalPhysicalDamage);
                }

                // Apply the calculated reduction.
                damage.DamageDict[type] = amount - reduction;

                // Consume used reduction from the pool.
                remainingReduction -= reduction;

                // Remove this full original amount from the running total
                // so later proportional calculations remain correct.
                totalPhysicalDamage -= amount;
            }

            return true;
        }

        // -- STAMINA AND SPRINT --
        private void UpdateSprintProgress(float frameTime, EntityUid uid, PhysicalPotentialComponent comp)
        {
            if (!TryComp<SprinterComponent>(uid, out var sprinter)
                || !sprinter.IsSprinting
                || !TryComp<InputMoverComponent>(uid, out var mover)
                || !mover.HasDirectionalMovement
                || !TryComp<PhysicsComponent>(uid, out var physics)
                || physics.LinearVelocity.LengthSquared() <= 0.01f)
            {
                comp.SprintTimer = 0;
                return;
            }

            comp.SprintTimer += frameTime;

            // Check if the sprint duration has exceeded the defined interval for a "tick"
            if (comp.SprintTimer > comp.SprintInterval)
            {
                comp.SprintTimer = 0;

                var newStrain = new TechnicalStrain { Stamina = comp.StaminaRisingSpeed };
                AddTechnicalStrain(comp, newStrain);
            }
        }
        #endregion

        #region Physical strains
        // -- PUSH-UP --
        private void OnStood(EntityUid uid, PhysicalPotentialComponent comp, StoodEvent args)
        {
           comp.LastStandTime = _timing.CurTime;
        }

        private void OnDowned(EntityUid uid, PhysicalPotentialComponent comp, DownedEvent args)
        {
            if ((_timing.CurTime - comp.LastStandTime).TotalSeconds < comp.PushUpWindow)
            {
                AddPhysicalStrain(comp, comp.PushUpsEfficiency * comp.PhysicalTrainingEfficiency);
                _popup.PopupEntity(Loc.GetString("system-physical-potential-push-up", ("gender", (object) GetGender(uid))), uid, uid);
            }
        }
        #endregion

        #region Calculate strains
        public void AddTechnicalStrain(PhysicalPotentialComponent comp, TechnicalStrain strain)
        {
            float efficiency = comp.TechnicalTrainingEfficiency;

            // Calculate the number of guaranteed additions (integer part)
            int fullExecutions = (int) MathF.Floor(efficiency);

            // Add strains as many times as the integer part allows
            for (int i = 0; i < fullExecutions; i++)
            {
                // Stop adding if the maximum strain limit is reached
                if (comp.TechnicalStrains.Count >= comp.MaxStrainsNumber) break;
                comp.TechnicalStrains.Add(strain);
            }

            // If there is still room in the list, process the fractional part
            if (comp.TechnicalStrains.Count < comp.MaxStrainsNumber)
            {
                // Calculate the remainder (e.g., 0.3 from 1.3)
                float remainder = comp.TechnicalTrainingEfficiency - (float) MathF.Floor(efficiency);

                // Add a "bonus" strain based on the probability of the remainder
                if (remainder > 0 && _random.Prob(remainder))
                {
                    comp.TechnicalStrains.Add(strain);
                }
            }

            ResetRestingTimer(comp);
        }

        public void AddPhysicalStrain(PhysicalPotentialComponent comp, float strain)
        {
            if(comp.PhysicalStrains.Count < comp.MaxStrainsNumber)
            {
                comp.PhysicalStrains.Add(strain * comp.PhysicalTrainingEfficiency);
            }

            ResetRestingTimer(comp);
        }

        public void ResetRestingTimer(PhysicalPotentialComponent comp)
        {
            // Reset the rest timer and set the cooldown period
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
            if (!comp.IsResting && comp.TechnicalStrains.Count > 0)
            {
                // Introduce a delay between iterations for smooth bonus progression 
                if (comp.NextStrainTime < _timing.CurTime)
                {
                    ApplyTechnicalStrain(uid, comp);
                    comp.NextStrainTime = _timing.CurTime + TimeSpan.FromSeconds(comp.StrainsApplyingDelay);
                }
            }
        }

        // Apply a specific strain point to the character's stats 
        private void ApplyTechnicalStrain(EntityUid uid, PhysicalPotentialComponent comp)
        {
            if (comp.TechnicalStrains.Count == 0) return;

            var strain = comp.TechnicalStrains[comp.TechnicalStrains.Count - 1];

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
            if (TryComp<StaminaComponent>(uid, out var stamina) && comp.StaminaBonus < comp.MaxStaminaBonus)
            {
                comp.StaminaBonus += comp.StaminaRisingSpeed;

                stamina.CritThreshold -= comp.CurrentStaminaBonus;
                comp.CurrentStaminaBonus = comp.StaminaBonus * comp.MuscleMass;
                stamina.CritThreshold += comp.CurrentStaminaBonus;
                Dirty(uid, stamina);
            }

            comp.TechnicalStrains.RemoveAt(comp.TechnicalStrains.Count - 1);

            Dirty(uid, comp);
        }

        private void OnSolutionChanged(Entity<SolutionComponent> ent, ref SolutionChangedEvent args)
        {
            // Get the owner uid
            var uid = Transform(ent).ParentUid;

            if (!TryComp<PhysicalPotentialComponent>(uid, out var comp))
                return;

            if (comp.PhysicalStrains.Count == 0 && comp.MuscleMass < comp.MaxMuscleMass)
                return;

            // Get the last recorded strain
            var strain = comp.PhysicalStrains[^1];

            // Access the solution that changed
            var solution = ent.Comp.Solution;

            // Get total amount of Protein reagent in the solution
            var protein = solution.GetTotalPrototypeQuantity("Protein");

            // Check if there is enough protein to trigger muscle growth
            if (protein >= comp.ProteinsCost)
            {
                // Remove the consumed protein from the solution
                solution.RemoveReagent("Protein", FixedPoint2.New(comp.ProteinsCost));

                // Increase muscle mass based on training efficiency
                comp.MuscleMass += comp.PhysicalTrainingEfficiency;

                if(comp.MuscleMass > comp.MaxMuscleMass) comp.MuscleMass = comp.MaxMuscleMass;
            }

            UpdateAlert(uid, comp);
            Dirty(uid, comp);
        }
        #endregion

        private void UpdateAlert(EntityUid uid, PhysicalPotentialComponent comp)
        {
            if(comp.MuscleMass >= 0.025f)
            {
                short stateIndex = (short) (comp.MuscleMass / comp.MaxMuscleMass * 9);

                _alertsSystem.ShowAlert(uid, "PhysicalPotential", stateIndex);
            }
            else
            {
                _alertsSystem.ClearAlert(uid, "PhysicalPotential");
            }
        }


        private void OnClone(Entity<PhysicalPotentialComponent> ent, ref CloningEvent args)
        {
            if (!args.Settings.EventComponents.Contains(Factory.GetRegistration(ent.Comp.GetType()).Name))
                return;

            var clone = EnsureComp<PhysicalPotentialComponent>(args.CloneUid);
            clone.TechnicalTrainingEfficiency = ent.Comp.TechnicalTrainingEfficiency;
            clone.TechnicalStrains = new List<TechnicalStrain>(ent.Comp.TechnicalStrains.Count);
            foreach (var strain in ent.Comp.TechnicalStrains)
            {
                clone.TechnicalStrains.Add(new TechnicalStrain
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
            clone.MaxStaminaBonus = ent.Comp.MaxStaminaBonus;
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
            clone.ProteinsCost = ent.Comp.ProteinsCost;

            if (TryComp<StaminaComponent>(args.CloneUid, out var stamina))
            {
                stamina.CritThreshold += clone.StaminaBonus;
                Dirty(args.CloneUid, stamina);
            }

            Dirty(args.CloneUid, clone);
        }

        private void OnExamine(EntityUid uid, PhysicalPotentialComponent comp, ExaminedEvent args)
        {
            if (comp.PowerLevel < 0.3f) return;

            string key = comp.PowerLevel switch
            {
                >= 0.8f => "system-physical-potential-examine-level3",
                >= 0.5f => "system-physical-potential-examine-level2",
                _ => "system-physical-potential-examine-level1",
            };

            args.PushMarkup(Loc.GetString(key, ("gender", (object) GetGender(uid))));
        }

        private Gender GetGender(EntityUid uid)
        {
            var entityGender = Gender.Neuter;

            if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            {
                entityGender = humanoid.Gender;
            }
            else if (TryComp<GrammarComponent>(uid, out var grammar))
            {
                entityGender = grammar.Gender ?? Gender.Neuter;
            }

            return entityGender;
        }

    }
}
