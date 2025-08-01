// SPDX-FileCopyrightText: 2023 Checkraze
// SPDX-FileCopyrightText: 2023 Nemanja
// SPDX-FileCopyrightText: 2023 Vordenburg
// SPDX-FileCopyrightText: 2023 deltanedas
// SPDX-FileCopyrightText: 2023 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2024 Dvir
// SPDX-FileCopyrightText: 2024 ErhardSteinhauer
// SPDX-FileCopyrightText: 2024 GreaseMonk
// SPDX-FileCopyrightText: 2024 MilenVolf
// SPDX-FileCopyrightText: 2024 Whatstone
// SPDX-FileCopyrightText: 2024 chavonadelal
// SPDX-FileCopyrightText: 2024 checkraze
// SPDX-FileCopyrightText: 2024 metalgearsloth
// SPDX-FileCopyrightText: 2025 starch
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Dataset;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;
using System.Linq; // Frontier

namespace Content.Shared.Salvage;

public abstract partial class SharedSalvageSystem : EntitySystem
{
    [Dependency] private readonly ILocalizationManager _loc = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    #region Descriptions

    public string GetMissionDescription(SalvageMission mission)
    {
        // Hardcoded in coooooz it's dynamic based on difficulty and I'm lazy.
        switch (mission.Mission)
        {
            case SalvageMissionType.Mining:
                // Taxation: , ("tax", $"{GetMiningTax(mission.Difficulty) * 100f:0}")
                return Loc.GetString("salvage-expedition-desc-mining");
            case SalvageMissionType.Destruction:
                var proto = _proto.Index<SalvageFactionPrototype>(mission.Faction).Configs["DefenseStructure"];

                return Loc.GetString("salvage-expedition-desc-structure",
                    ("count", GetStructureCount(mission.Difficulty)),
                    ("structure", _loc.GetEntityData(proto).Name));
            case SalvageMissionType.Elimination:
                return Loc.GetString("salvage-expedition-desc-elimination");
            default:
                throw new NotImplementedException();
        }
    }

    public float GetMiningTax(DifficultyRating baseRating)
    {
        return 0.6f + (int) baseRating * 0.05f;
    }

    /// <summary>
    /// Gets the amount of structures to destroy.
    /// </summary>
    public int GetStructureCount(DifficultyRating baseRating)
    {
        return 1 + (int) baseRating * 2;
    }

    #endregion

    public int GetDifficulty(DifficultyRating rating)
    {
        switch (rating)
        {
            case DifficultyRating.Minimal:
                return 8;
            case DifficultyRating.Minor:
                return 12;
            case DifficultyRating.Moderate:
                return 16;
            case DifficultyRating.Hazardous:
                return 20;
            case DifficultyRating.Extreme:
                return 30;
            default:
                throw new ArgumentOutOfRangeException(nameof(rating), rating, null);
        }
    }

    /// <summary>
    /// How many groups of mobs to spawn for a mission.
    /// </summary>
    public float GetSpawnCount(DifficultyRating difficulty)
    {
        return ((int)difficulty + 1) * 2; // Frontier: add one to difficulty (no empty expeditions)
    }

    public string GetFTLName(LocalizedDatasetPrototype dataset, int seed)
    {
        var random = new System.Random(seed);
        return $"{Loc.GetString(dataset.Values[random.Next(dataset.Values.Count)])}-{random.Next(10, 100)}-{(char) (65 + random.Next(26))}";
    }

    public SalvageMission GetMission(SalvageMissionType config, DifficultyRating difficulty, int seed)
    {
        // This is on shared to ensure the client display for missions and what the server generates are consistent
        var rating = (float) GetDifficulty(difficulty);
        // Don't want easy missions to have any negative modifiers but also want
        // easy to be a 1 for difficulty.
        rating -= 1f;
        var rand = new System.Random(seed);

        // Run budget in order of priority
        // - Biome
        // - Lighting
        // - Atmos
        var faction = GetMod<SalvageFactionPrototype>(rand, ref rating);
        var biome = GetMod<SalvageBiomeMod>(rand, ref rating);
        var air = GetBiomeMod<SalvageAirMod>(biome.ID, rand, ref rating);
        var dungeon = GetBiomeMod<SalvageDungeonModPrototype>(biome.ID, rand, ref rating);

        var mods = new List<string>();

        if (air.Description != string.Empty)
        {
            mods.Add(Loc.GetString(air.Description));
        }

        // only show the description if there is an atmosphere since wont matter otherwise
        var temp = GetBiomeMod<SalvageTemperatureMod>(biome.ID, rand, ref rating);
        if (temp.Description != string.Empty && !air.Space)
        {
            mods.Add(Loc.GetString(temp.Description));
        }

        // only show the description if there is an atmosphere since wont matter otherwise
        var weather = GetBiomeMod<SalvageWeatherMod>(biome.ID, rand, ref rating);
        if (weather.Description != string.Empty && !air.Space)
        {
            mods.Add(Loc.GetString(weather.Description));
        }

        var light = GetBiomeMod<SalvageLightMod>(biome.ID, rand, ref rating);
        if (light.Description != string.Empty)
        {
            mods.Add(Loc.GetString(light.Description));
        }

        var time = GetMod<SalvageTimeMod>(rand, ref rating);
        // Round the duration to nearest 15 seconds.
        var exactDuration = MathHelper.Lerp(time.MinDuration, time.MaxDuration, rand.NextFloat());
        exactDuration = MathF.Round(exactDuration / 15f) * 15f;
        var duration = TimeSpan.FromSeconds(exactDuration);

        if (!time.Hidden && time.Description != string.Empty)
        {
            mods.Add(Loc.GetString(time.Description));
        }

        var rewards = GetRewards(difficulty, rand);
        return new SalvageMission(seed, difficulty, dungeon.ID, faction.ID, config, biome.ID, weather.ID, air.ID, temp.Temperature, light.Color, duration, rewards, mods);
    }

    public T GetBiomeMod<T>(string biome, System.Random rand, ref float rating) where T : class, IPrototype, IBiomeSpecificMod
    {
        var mods = _proto.EnumeratePrototypes<T>().ToList();
        mods.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));
        rand.Shuffle(mods);

        foreach (var mod in mods)
        {
            if (mod.Cost > rating || (mod.Biomes != null && !mod.Biomes.Contains(biome)))
                continue;

            rating -= mod.Cost;

            return mod;
        }

        throw new InvalidOperationException();
    }

    public T GetMod<T>(System.Random rand, ref float rating) where T : class, IPrototype, ISalvageMod
    {
        var mods = _proto.EnumeratePrototypes<T>().ToList();
        mods.Sort((x, y) => string.Compare(x.ID, y.ID, StringComparison.Ordinal));
        rand.Shuffle(mods);

        foreach (var mod in mods)
        {
            if (mod.Cost > rating)
                continue;

            rating -= mod.Cost;

            return mod;
        }

        throw new InvalidOperationException();
    }

    private List<string> GetRewards(DifficultyRating difficulty, System.Random rand)
    {
        var rewards = new List<string>(3);
        var ids = RewardsForDifficulty(difficulty);

        foreach (var id in ids)
        {
            // pick a random reward to give
            var weights = _proto.Index<WeightedRandomEntityPrototype>(id);
            rewards.Add(weights.Pick(rand));
        }

        return rewards;
    }

    /// <summary>
    /// Get a list of WeightedRandomEntityPrototype IDs with the rewards for a certain difficulty.
    /// Frontier: added uncommon and legendary reward tiers, limited amount of rewards to 1 per difficulty rating
    /// </summary>
    private string[] RewardsForDifficulty(DifficultyRating rating)
    {
        var t1 = "ExpeditionRewardT1"; // Frontier - Update tiers
        var t2 = "ExpeditionRewardT2"; // Frontier - Update tiers
        var t3 = "ExpeditionRewardT3"; // Frontier - Update tiers
        var t4 = "ExpeditionRewardT4"; // Frontier - Update tiers
        var t5 = "ExpeditionRewardT5"; // Frontier - Update tiers
        switch (rating)
        {
            case DifficultyRating.Minimal:
                return new string[] { t1 }; // Frontier - Update tiers // Frontier
            case DifficultyRating.Minor:
                return new string[] { t2 }; // Frontier - Update tiers // Frontier
            case DifficultyRating.Moderate:
                return new string[] { t3 }; // Frontier - Update tiers
            case DifficultyRating.Hazardous:
                return new string[] { t4 }; // Frontier - Update tiers
            case DifficultyRating.Extreme:
                return new string[] { t5 }; // Frontier - Update tiers
            default:
                throw new NotImplementedException();
        }
    }
}

[Serializable, NetSerializable]
public enum SalvageMissionType : byte
{
    /// <summary>
    /// No dungeon, just ore loot and random mob spawns.
    /// </summary>
    Mining,

    /// <summary>
    /// Destroy the specified structures in a dungeon.
    /// </summary>
    Destruction,

    /// <summary>
    /// Kill a large creature in a dungeon.
    /// </summary>
    Elimination,
}

[Serializable, NetSerializable]
public enum DifficultyRating : byte
{
    Minimal,
    Minor,
    Moderate,
    Hazardous,
    Extreme,
}
