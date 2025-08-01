// SPDX-FileCopyrightText: 2024 slarticodefast
// SPDX-FileCopyrightText: 2025 starch
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage.Components;

namespace Content.Shared.Damage.Systems;

public sealed class DamageProtectionBuffSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageProtectionBuffComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(EntityUid uid, DamageProtectionBuffComponent component, DamageModifyEvent args)
    {
        foreach (var modifier in component.Modifiers.Values)
            args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage,
                DamageSpecifier.PenetrateArmor(modifier, args.ArmorPenetration)); // Goob edit
    }
}
