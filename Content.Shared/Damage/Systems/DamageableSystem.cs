// SPDX-FileCopyrightText: 2021 20kdc
// SPDX-FileCopyrightText: 2021 Acruid
// SPDX-FileCopyrightText: 2021 DrSmugleaf
// SPDX-FileCopyrightText: 2021 Javier Guardia Fernández
// SPDX-FileCopyrightText: 2021 Vera Aguilera Puerto
// SPDX-FileCopyrightText: 2022 Alex Evgrashin
// SPDX-FileCopyrightText: 2022 CommieFlowers
// SPDX-FileCopyrightText: 2022 EmoGarbage404
// SPDX-FileCopyrightText: 2022 Flipp Syder
// SPDX-FileCopyrightText: 2022 Paul Ritter
// SPDX-FileCopyrightText: 2022 Rane
// SPDX-FileCopyrightText: 2022 ShadowCommander
// SPDX-FileCopyrightText: 2022 Visne
// SPDX-FileCopyrightText: 2022 mirrorcult
// SPDX-FileCopyrightText: 2022 rolfero
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 Jezithyr
// SPDX-FileCopyrightText: 2023 Kara
// SPDX-FileCopyrightText: 2023 Leon Friedrich
// SPDX-FileCopyrightText: 2023 Nemanja
// SPDX-FileCopyrightText: 2023 PixelTK
// SPDX-FileCopyrightText: 2023 Slava0135
// SPDX-FileCopyrightText: 2023 deltanedas
// SPDX-FileCopyrightText: 2023 metalgearsloth
// SPDX-FileCopyrightText: 2024 Pieter-Jan Briers
// SPDX-FileCopyrightText: 2025 ActiveMammmoth
// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 SlamBamActionman
// SPDX-FileCopyrightText: 2025 starch
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared.CCVar;
using Content.Shared.Chemistry;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radiation.Events;
using Content.Shared.Rejuvenate;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

// Shitmed Change
using Content.Shared.Body.Systems;
using Content.Shared._Shitmed.Targeting;
using Robust.Shared.Random;

namespace Content.Shared.Damage
{
    public sealed class DamageableSystem : EntitySystem
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
        [Dependency] private readonly INetManager _netMan = default!;
        [Dependency] private readonly SharedBodySystem _body = default!; // Shitmed Change
        [Dependency] private readonly IRobustRandom _random = default!; // Shitmed Change
        [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;
        [Dependency] private readonly IConfigurationManager _config = default!;
        [Dependency] private readonly SharedChemistryGuideDataSystem _chemistryGuideData = default!;

        private EntityQuery<AppearanceComponent> _appearanceQuery;
        private EntityQuery<DamageableComponent> _damageableQuery;
        private EntityQuery<MindContainerComponent> _mindContainerQuery;

        public float UniversalAllDamageModifier { get; private set; } = 1f;
        public float UniversalAllHealModifier { get; private set; } = 1f;
        public float UniversalMeleeDamageModifier { get; private set; } = 1f;
        public float UniversalProjectileDamageModifier { get; private set; } = 1f;
        public float UniversalHitscanDamageModifier { get; private set; } = 1f;
        public float UniversalReagentDamageModifier { get; private set; } = 1f;
        public float UniversalReagentHealModifier { get; private set; } = 1f;
        public float UniversalExplosionDamageModifier { get; private set; } = 1f;
        public float UniversalThrownDamageModifier { get; private set; } = 1f;
        public float UniversalTopicalsHealModifier { get; private set; } = 1f;

        public override void Initialize()
        {
            SubscribeLocalEvent<DamageableComponent, ComponentInit>(DamageableInit);
            SubscribeLocalEvent<DamageableComponent, ComponentHandleState>(DamageableHandleState);
            SubscribeLocalEvent<DamageableComponent, ComponentGetState>(DamageableGetState);
            SubscribeLocalEvent<DamageableComponent, OnIrradiatedEvent>(OnIrradiated);
            SubscribeLocalEvent<DamageableComponent, RejuvenateEvent>(OnRejuvenate);

            _appearanceQuery = GetEntityQuery<AppearanceComponent>();
            _damageableQuery = GetEntityQuery<DamageableComponent>();
            _mindContainerQuery = GetEntityQuery<MindContainerComponent>();

            // Damage modifier CVars are updated and stored here to be queried in other systems.
            // Note that certain modifiers requires reloading the guidebook.
            Subs.CVar(_config, CCVars.PlaytestAllDamageModifier, value =>
            {
                UniversalAllDamageModifier = value;
                _chemistryGuideData.ReloadAllReagentPrototypes();
            }, true);
            Subs.CVar(_config, CCVars.PlaytestAllHealModifier, value =>
            {
                UniversalAllHealModifier = value;
                _chemistryGuideData.ReloadAllReagentPrototypes();
            }, true);
            Subs.CVar(_config, CCVars.PlaytestProjectileDamageModifier, value => UniversalProjectileDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestMeleeDamageModifier, value => UniversalMeleeDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestProjectileDamageModifier, value => UniversalProjectileDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestHitscanDamageModifier, value => UniversalHitscanDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestReagentDamageModifier, value =>
            {
                UniversalReagentDamageModifier = value;
                _chemistryGuideData.ReloadAllReagentPrototypes();
            }, true);
            Subs.CVar(_config, CCVars.PlaytestReagentHealModifier, value =>
            {
                 UniversalReagentHealModifier = value;
                 _chemistryGuideData.ReloadAllReagentPrototypes();
            }, true);
            Subs.CVar(_config, CCVars.PlaytestExplosionDamageModifier, value => UniversalExplosionDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestThrownDamageModifier, value => UniversalThrownDamageModifier = value, true);
            Subs.CVar(_config, CCVars.PlaytestTopicalsHealModifier, value => UniversalTopicalsHealModifier = value, true);
        }

        /// <summary>
        ///     Initialize a damageable component
        /// </summary>
        private void DamageableInit(EntityUid uid, DamageableComponent component, ComponentInit _)
        {
            if (component.DamageContainerID != null &&
                _prototypeManager.TryIndex<DamageContainerPrototype>(component.DamageContainerID,
                out var damageContainerPrototype))
            {
                // Initialize damage dictionary, using the types and groups from the damage
                // container prototype
                foreach (var type in damageContainerPrototype.SupportedTypes)
                {
                    component.Damage.DamageDict.TryAdd(type, FixedPoint2.Zero);
                }

                foreach (var groupId in damageContainerPrototype.SupportedGroups)
                {
                    var group = _prototypeManager.Index<DamageGroupPrototype>(groupId);
                    foreach (var type in group.DamageTypes)
                    {
                        component.Damage.DamageDict.TryAdd(type, FixedPoint2.Zero);
                    }
                }
            }
            else
            {
                // No DamageContainerPrototype was given. So we will allow the container to support all damage types
                foreach (var type in _prototypeManager.EnumeratePrototypes<DamageTypePrototype>())
                {
                    component.Damage.DamageDict.TryAdd(type.ID, FixedPoint2.Zero);
                }
            }

            component.Damage.GetDamagePerGroup(_prototypeManager, component.DamagePerGroup);
            component.TotalDamage = component.Damage.GetTotal();
        }

        /// <summary>
        ///     Directly sets the damage specifier of a damageable component.
        /// </summary>
        /// <remarks>
        ///     Useful for some unfriendly folk. Also ensures that cached values are updated and that a damage changed
        ///     event is raised.
        /// </remarks>
        public void SetDamage(EntityUid uid, DamageableComponent damageable, DamageSpecifier damage)
        {
            damageable.Damage = damage;
            DamageChanged(uid, damageable);
        }

        /// <summary>
        ///     If the damage in a DamageableComponent was changed, this function should be called.
        /// </summary>
        /// <remarks>
        ///     This updates cached damage information, flags the component as dirty, and raises a damage changed event.
        ///     The damage changed event is used by other systems, such as damage thresholds.
        /// </remarks>
        public void DamageChanged(EntityUid uid, DamageableComponent component, DamageSpecifier? damageDelta = null,
            bool interruptsDoAfters = true, EntityUid? origin = null, bool? canSever = null, float armorPenetration = 0f) // Shitmed Change
        {
            component.Damage.GetDamagePerGroup(_prototypeManager, component.DamagePerGroup);
            component.TotalDamage = component.Damage.GetTotal();
            Dirty(uid, component);

            if (_appearanceQuery.TryGetComponent(uid, out var appearance) && damageDelta != null)
            {
                var data = new DamageVisualizerGroupData(component.DamagePerGroup.Keys.ToList());
                _appearance.SetData(uid, DamageVisualizerKeys.DamageUpdateGroups, data, appearance);
            }
            RaiseLocalEvent(uid, new DamageChangedEvent(component, damageDelta, interruptsDoAfters, origin, canSever ?? true)); // Shitmed Change
        }

        /// <summary>
        ///     Applies damage specified via a <see cref="DamageSpecifier"/>.
        /// </summary>
        /// <remarks>
        ///     <see cref="DamageSpecifier"/> is effectively just a dictionary of damage types and damage values. This
        ///     function just applies the container's resistances (unless otherwise specified) and then changes the
        ///     stored damage data. Division of group damage into types is managed by <see cref="DamageSpecifier"/>.
        /// </remarks>
        /// <returns>
        ///     Returns a <see cref="DamageSpecifier"/> with information about the actual damage changes. This will be
        ///     null if the user had no applicable components that can take damage.
        /// </returns>
        public DamageSpecifier? TryChangeDamage(EntityUid? uid, DamageSpecifier damage, bool ignoreResistances = false,
            bool interruptsDoAfters = true, DamageableComponent? damageable = null, EntityUid? origin = null, float armorPenetration = 0f,
            // Shitmed Change
            bool? canSever = true, bool? canEvade = false, float? partMultiplier = 1.00f, TargetBodyPart? targetPart = null)
        {
            if (!uid.HasValue || !_damageableQuery.Resolve(uid.Value, ref damageable, false))
            {
                // TODO BODY SYSTEM pass damage onto body system
                return null;
            }

            if (damage.Empty)
            {
                return damage;
            }

            var before = new BeforeDamageChangedEvent(damage, origin, targetPart); // Shitmed Change
            RaiseLocalEvent(uid.Value, ref before);

            if (before.Cancelled)
                return null;

            // Shitmed Change Start
            var partDamage = new TryChangePartDamageEvent(damage, origin, targetPart, ignoreResistances, canSever ?? true, canEvade ?? false, partMultiplier ?? 1.00f);
            RaiseLocalEvent(uid.Value, ref partDamage);

            if (partDamage.Evaded || partDamage.Cancelled)
                return null;

            // Shitmed Change End

            // Apply resistances
            if (!ignoreResistances)
            {
                if (damageable.DamageModifierSetId != null &&
                    _prototypeManager.TryIndex<DamageModifierSetPrototype>(damageable.DamageModifierSetId, out var modifierSet))
                {
                    // TODO DAMAGE PERFORMANCE
                    // use a local private field instead of creating a new dictionary here..
                    // TODO: We need to add a check to see if the given armor covers the targeted part (if any) to modify or not.
                    damage = DamageSpecifier.ApplyModifierSet(damage,
                        DamageSpecifier.PenetrateArmor(modifierSet, armorPenetration)); // Goob edit
                }

                var ev = new DamageModifyEvent(damage, origin, armorPenetration, targetPart); // Shitmed Change
                RaiseLocalEvent(uid.Value, ev);
                damage = ev.Damage;

                if (damage.Empty)
                {
                    return damage;
                }
            }

            damage = ApplyUniversalAllModifiers(damage);

            // TODO DAMAGE PERFORMANCE
            // Consider using a local private field instead of creating a new dictionary here.
            // Would need to check that nothing ever tries to cache the delta.
            var delta = new DamageSpecifier();
            delta.DamageDict.EnsureCapacity(damage.DamageDict.Count);

            var dict = damageable.Damage.DamageDict;
            foreach (var (type, value) in damage.DamageDict)
            {
                // CollectionsMarshal my beloved.
                if (!dict.TryGetValue(type, out var oldValue))
                    continue;

                var newValue = FixedPoint2.Max(FixedPoint2.Zero, oldValue + value);
                if (newValue == oldValue)
                    continue;

                dict[type] = newValue;
                delta.DamageDict[type] = newValue - oldValue;
            }

            if (delta.DamageDict.Count > 0)
                DamageChanged(uid.Value, damageable, delta, interruptsDoAfters, origin, canSever); // Shitmed Change

            return delta;
        }

        /// <summary>
        /// Applies the two univeral "All" modifiers, if set.
        /// Individual damage source modifiers are set in their respective code.
        /// </summary>
        /// <param name="damage">The damage to be changed.</param>
        public DamageSpecifier ApplyUniversalAllModifiers(DamageSpecifier damage)
        {
            // Checks for changes first since they're unlikely in normal play.
            if (UniversalAllDamageModifier == 1f && UniversalAllHealModifier == 1f)
                return damage;

            foreach (var (key, value) in damage.DamageDict)
            {
                if (value == 0)
                    continue;

                if (value > 0)
                {
                    damage.DamageDict[key] *= UniversalAllDamageModifier;
                    continue;
                }

                if (value < 0)
                {
                    damage.DamageDict[key] *= UniversalAllHealModifier;
                }
            }

            return damage;
        }

        /// <summary>
        ///     Sets all damage types supported by a <see cref="DamageableComponent"/> to the specified value.
        /// </summary>
        /// <remakrs>
        ///     Does nothing If the given damage value is negative.
        /// </remakrs>
        public void SetAllDamage(EntityUid uid, DamageableComponent component, FixedPoint2 newValue)
        {
            if (newValue < 0)
            {
                // invalid value
                return;
            }

            foreach (var type in component.Damage.DamageDict.Keys)
            {
                component.Damage.DamageDict[type] = newValue;
            }

            // Setting damage does not count as 'dealing' damage, even if it is set to a larger value, so we pass an
            // empty damage delta.
            DamageChanged(uid, component, new DamageSpecifier());

            // Shitmed Change Start
            if (HasComp<TargetingComponent>(uid))
            {
                foreach (var (part, _) in _body.GetBodyChildren(uid))
                {
                    if (!TryComp(part, out DamageableComponent? damageComp))
                        continue;

                    SetAllDamage(part, damageComp, newValue);
                }
            }
            // Shitmed Change End
        }

        public void SetDamageModifierSetId(EntityUid uid, string damageModifierSetId, DamageableComponent? comp = null)
        {
            if (!_damageableQuery.Resolve(uid, ref comp))
                return;

            comp.DamageModifierSetId = damageModifierSetId;
            Dirty(uid, comp);
        }

        private void DamageableGetState(EntityUid uid, DamageableComponent component, ref ComponentGetState args)
        {
            if (_netMan.IsServer)
            {
                args.State = new DamageableComponentState(component.Damage.DamageDict, component.DamageContainerID, component.DamageModifierSetId, component.HealthBarThreshold);
            }
            else
            {
                // avoid mispredicting damage on newly spawned entities.
                args.State = new DamageableComponentState(component.Damage.DamageDict.ShallowClone(), component.DamageContainerID, component.DamageModifierSetId, component.HealthBarThreshold);
            }
        }

        private void OnIrradiated(EntityUid uid, DamageableComponent component, OnIrradiatedEvent args)
        {
            var damageValue = FixedPoint2.New(args.TotalRads);

            // Radiation should really just be a damage group instead of a list of types.
            DamageSpecifier damage = new();
            foreach (var typeId in component.RadiationDamageTypeIDs)
            {
                damage.DamageDict.Add(typeId, damageValue);
            }

            TryChangeDamage(uid, damage, interruptsDoAfters: false);
        }

        private void OnRejuvenate(EntityUid uid, DamageableComponent component, RejuvenateEvent args)
        {
            TryComp<MobThresholdsComponent>(uid, out var thresholds);
            _mobThreshold.SetAllowRevives(uid, true, thresholds); // do this so that the state changes when we set the damage
            SetAllDamage(uid, component, 0);
            _mobThreshold.SetAllowRevives(uid, false, thresholds);
        }

        private void DamageableHandleState(EntityUid uid, DamageableComponent component, ref ComponentHandleState args)
        {
            if (args.Current is not DamageableComponentState state)
            {
                return;
            }

            component.DamageContainerID = state.DamageContainerId;
            component.DamageModifierSetId = state.ModifierSetId;
            component.HealthBarThreshold = state.HealthBarThreshold;

            // Has the damage actually changed?
            DamageSpecifier newDamage = new() { DamageDict = new(state.DamageDict) };
            var delta = component.Damage - newDamage;
            delta.TrimZeros();

            if (!delta.Empty)
            {
                component.Damage = newDamage;
                DamageChanged(uid, component, delta);
            }
        }
    }

    /// <summary>
    ///     Raised before damage is done, so stuff can cancel it if necessary.
    /// </summary>
    [ByRefEvent]
    public record struct BeforeDamageChangedEvent(
        DamageSpecifier Damage,
        EntityUid? Origin = null,
        TargetBodyPart? TargetPart = null, // Shitmed Change
        bool Cancelled = false);

    /// <summary>
    ///     Shitmed Change: Raised on parts before damage is done so we can cancel the damage if they evade.
    /// </summary>
    [ByRefEvent]
    public record struct TryChangePartDamageEvent(
        DamageSpecifier Damage,
        EntityUid? Origin = null,
        TargetBodyPart? TargetPart = null,
        bool IgnoreResistances = false,
        bool CanSever = true,
        bool CanEvade = false,
        float PartMultiplier = 1.00f,
        bool Evaded = false,
        bool Cancelled = false);

    /// <summary>
    ///     Raised on an entity when damage is about to be dealt,
    ///     in case anything else needs to modify it other than the base
    ///     damageable component.
    ///
    ///     For example, armor.
    /// </summary>
    public sealed class DamageModifyEvent : EntityEventArgs, IInventoryRelayEvent
    {
        // Whenever locational damage is a thing, this should just check only that bit of armour.
        public SlotFlags TargetSlots { get; } = ~SlotFlags.POCKET;

        public readonly DamageSpecifier OriginalDamage;
        public DamageSpecifier Damage;
        public EntityUid? Origin;
        public float ArmorPenetration; // Goobstation
        public readonly TargetBodyPart? TargetPart; // Shitmed Change

        public DamageModifyEvent(DamageSpecifier damage, EntityUid? origin = null, float armorPenetration = 0, TargetBodyPart? targetPart = null) // Shitmed Change
        {
            OriginalDamage = damage;
            Damage = damage;
            Origin = origin;
            TargetPart = targetPart; // Shitmed Change
            ArmorPenetration = armorPenetration; // Goobstation
        }
    }

    public sealed class DamageChangedEvent : EntityEventArgs
    {
        /// <summary>
        ///     This is the component whose damage was changed.
        /// </summary>
        /// <remarks>
        ///     Given that nearly every component that cares about a change in the damage, needs to know the
        ///     current damage values, directly passing this information prevents a lot of duplicate
        ///     Owner.TryGetComponent() calls.
        /// </remarks>
        public readonly DamageableComponent Damageable;

        /// <summary>
        ///     The amount by which the damage has changed. If the damage was set directly to some number, this will be
        ///     null.
        /// </summary>
        public readonly DamageSpecifier? DamageDelta;

        /// <summary>
        ///     Was any of the damage change dealing damage, or was it all healing?
        /// </summary>
        public readonly bool DamageIncreased;

        /// <summary>
        ///     Does this event interrupt DoAfters?
        ///     Note: As provided in the constructor, this *does not* account for DamageIncreased.
        ///     As written into the event, this *does* account for DamageIncreased.
        /// </summary>
        public readonly bool InterruptsDoAfters;

        /// <summary>
        ///     Contains the entity which caused the change in damage, if any was responsible.
        /// </summary>
        public readonly EntityUid? Origin;

        /// <summary>
        ///     Shitmed Change: Can this damage event sever parts?
        /// </summary>
        public readonly bool CanSever;

        public DamageChangedEvent(DamageableComponent damageable, DamageSpecifier? damageDelta, bool interruptsDoAfters, EntityUid? origin, bool canSever = true) // Shitmed Change
        {
            Damageable = damageable;
            DamageDelta = damageDelta;
            Origin = origin;
            CanSever = canSever; // Shitmed Change
            if (DamageDelta == null)
                return;

            foreach (var damageChange in DamageDelta.DamageDict.Values)
            {
                if (damageChange > 0)
                {
                    DamageIncreased = true;
                    break;
                }
            }
            InterruptsDoAfters = interruptsDoAfters && DamageIncreased;
        }
    }
}
