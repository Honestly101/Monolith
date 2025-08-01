// SPDX-FileCopyrightText: 2023 Checkraze
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2024 Dvir
// SPDX-FileCopyrightText: 2024 MilenVolf
// SPDX-FileCopyrightText: 2024 SlamBamActionman
// SPDX-FileCopyrightText: 2024 Whatstone
// SPDX-FileCopyrightText: 2024 Wiebe Geertsma
// SPDX-FileCopyrightText: 2024 checkraze
// SPDX-FileCopyrightText: 2024 metalgearsloth
// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 GreaseMonk
// SPDX-FileCopyrightText: 2025 Redrover1760
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Station.Components;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Content.Server.Salvage.Expeditions; // Frontier
using Content.Shared._NF.CCVar; // Frontier
using Content.Shared.Mind.Components; // Frontier
using Content.Shared.Mobs.Components; // Frontier
using Content.Shared.NPC.Components; // Frontier
using Content.Shared.IdentityManagement; // Frontier
using Content.Shared.NPC; // Frontier
using Content.Server._NF.Salvage; // Frontier
using Content.Server.Shuttles.Components;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    [ValidatePrototypeId<EntityPrototype>]
    public const string CoordinatesDisk = "CoordinatesDisk";
    private const float ShuttleFTLRange = 256f;
    private const float ShuttleFTLMassThreshold = 100f;

    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    private void OnSalvageClaimMessage(EntityUid uid, SalvageExpeditionConsoleComponent component, ClaimSalvageMessage args)
    {
        var station = _station.GetOwningStation(uid);

        // Frontier
        if (!TryComp<SalvageExpeditionDataComponent>(station, out var data) || data.Claimed) // Moved up before the active expedition count
            return;

        var activeExpeditionCount = 0;
        var expeditionQuery = AllEntityQuery<SalvageExpeditionDataComponent, MetaDataComponent>();
        while (expeditionQuery.MoveNext(out var expeditionUid, out _, out _))
            if (TryComp<SalvageExpeditionDataComponent>(expeditionUid, out var expeditionData) && expeditionData.Claimed)
                activeExpeditionCount++;

        if (activeExpeditionCount >= _cfgManager.GetCVar(NFCCVars.SalvageExpeditionMaxActive))
        {
            PlayDenySound(uid, component);
            _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-too-many"), uid, PopupType.MediumCaution);
            UpdateConsoles(station.Value, data);
            return;
        }
        // End Frontier

        if (!data.Missions.TryGetValue(args.Index, out var missionparams))
            return;

        // Frontier: FTL travel is currently restricted to expeditions and such, and so we need to put this here
        // until FTL changes for us in some way.
        if (!component.Debug) // Skip the test
        {
            if (!TryComp<StationDataComponent>(station, out var stationData))
                return;
            if (_station.GetLargestGrid(stationData) is not { Valid: true } grid)
                return;
            if (!TryComp<MapGridComponent>(grid, out var gridComp))
                return;

            // Frontier: check for FTL component - if one exists, the station won't be taken into FTL.
            if (HasComp<FTLComponent>(grid))
            {
                PlayDenySound(uid, component);
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-recharge"), uid, PopupType.MediumCaution);
                UpdateConsoles(station.Value, data); // Sure, why not?
                return;
            }

            var xform = Transform(grid);
            var bounds = xform.WorldMatrix.TransformBox(gridComp.LocalAABB).Enlarged(ShuttleFTLRange);
            var bodyQuery = GetEntityQuery<PhysicsComponent>();
            // Keep track of docked grids to exclude them from the proximity check
            var dockedGrids = new HashSet<EntityUid>();

            // Find all docked grids by looking for DockingComponents on the shuttle
            var dockQuery = EntityQueryEnumerator<DockingComponent, TransformComponent>();
            while (dockQuery.MoveNext(out var dockUid, out var dock, out var dockXform))
            {
                // Only consider docks on our grid
                if (dockXform.GridUid != grid || !dock.Docked || dock.DockedWith == null)
                    continue;

                // If we have a docked entity, get its grid
                if (TryComp<TransformComponent>(dock.DockedWith.Value, out var dockedXform) && dockedXform.GridUid != null)
                {
                    dockedGrids.Add(dockedXform.GridUid.Value);

                    // Check if we're docked to another grid
                    var parentGridUid = dockedXform.GridUid.Value;

                    // Find all other grids docked to this parent grid
                    // These should also be excluded from the proximity check so we can
                    // still FTL even when other ships are docked to the same station/grid
                    var parentDockQuery = EntityQueryEnumerator<DockingComponent, TransformComponent>();
                    while (parentDockQuery.MoveNext(out var parentDockUid, out var parentDock, out var parentDockXform))
                    {
                        // Only consider docks on the parent grid
                        if (parentDockXform.GridUid != parentGridUid || !parentDock.Docked || parentDock.DockedWith == null)
                            continue;

                        // If we have a docked entity and it's not our grid, add its grid to the exclusion list
                        if (TryComp<TransformComponent>(parentDock.DockedWith.Value, out var siblingDockedXform) &&
                            siblingDockedXform.GridUid != null &&
                            siblingDockedXform.GridUid != grid)
                        {
                            dockedGrids.Add(siblingDockedXform.GridUid.Value);
                        }
                    }
                }
            }

            foreach (var other in _mapManager.FindGridsIntersecting(xform.MapID, bounds))
            {
                if (other.Owner == grid ||
                    dockedGrids.Contains(other.Owner) || // Skip grids that are docked to us or to the same parent grid
                    !bodyQuery.TryGetComponent(other.Owner, out var body) ||
                    body.Mass < ShuttleFTLMassThreshold)
                {
                    continue;
                }

                PlayDenySound(uid, component);
                _popupSystem.PopupEntity(Loc.GetString("shuttle-ftl-proximity"), uid, PopupType.Medium);
                UpdateConsoles(station.Value, data);
                return;
            }
        }
        // End Frontier

        // Frontier  change - disable coordinate disks for expedition missions
        //var cdUid = Spawn(CoordinatesDisk, Transform(uid).Coordinates);
        SpawnMission(missionparams, station.Value, null);

        data.ActiveMission = args.Index;
        var mission = GetMission(missionparams.MissionType, missionparams.Difficulty, missionparams.Seed);
        data.NextOffer = _timing.CurTime + mission.Duration + TimeSpan.FromSeconds(1);

        // Frontier  change - disable coordinate disks for expedition missions
        //_labelSystem.Label(cdUid, GetFTLName(_prototypeManager.Index<LocalizedDatasetPrototype>("NamesBorer"), missionparams.Seed));
        //_audio.PlayPvs(component.PrintSound, uid);

        UpdateConsoles(station.Value, data); // Frontier: add station
    }

    // Frontier: early expedition end
    private void OnSalvageFinishMessage(EntityUid entity, SalvageExpeditionConsoleComponent component, FinishSalvageMessage e)
    {
        var station = _station.GetOwningStation(entity);
        if (!TryComp<SalvageExpeditionDataComponent>(station, out var data) || !data.CanFinish)
            return;

        // Based on SalvageSystem.Runner:OnConsoleFTLAttempt
        if (!TryComp(entity, out TransformComponent? xform)) // Get the console's grid (if you move it, rip you)
        {
            PlayDenySound(entity, component);
            _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-shuttle-not-found"), entity, PopupType.MediumCaution);
            UpdateConsoles(station.Value, data);
            return;
        }

        // Frontier: check if any player characters or friendly ghost roles are outside
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var mindContainer, out var _, out var mobXform))
        {
            if (mobXform.MapUid != xform.MapUid)
                continue;

            // Not player controlled (ghosted)
            if (!mindContainer.HasMind)
                continue;

            // NPC, definitely not a person
            if (HasComp<ActiveNPCComponent>(uid) || HasComp<NFSalvageMobRestrictionsComponent>(uid))
                continue;

            // Hostile ghost role, continue
            if (TryComp(uid, out NpcFactionMemberComponent? npcFaction))
            {
                var hostileFactions = npcFaction.HostileFactions;
                if (hostileFactions.Contains("NanoTrasen")) // Nasty - what if we need pirate expeditions?
                    continue;
            }

            // Okay they're on salvage, so are they on the shuttle.
            if (mobXform.GridUid != xform.GridUid)
            {
                PlayDenySound(entity, component);
                _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-not-everyone-aboard", ("target", Identity.Entity(uid, EntityManager))), entity, PopupType.MediumCaution);
                UpdateConsoles(station.Value, data);
                return;
            }
        }
        // End SalvageSystem.Runner:OnConsoleFTLAttempt

        data.CanFinish = false;
        UpdateConsoles(station.Value, data);

        var map = Transform(entity).MapUid;

        if (!TryComp<SalvageExpeditionComponent>(map, out var expedition))
            return;

        const int departTime = 20;
        var newEndTime = _timing.CurTime + TimeSpan.FromSeconds(departTime);

        if (expedition.EndTime <= newEndTime)
            return;

        expedition.EndTime = newEndTime;
        expedition.Stage = ExpeditionStage.FinalCountdown;

        Announce(map.Value, Loc.GetString("salvage-expedition-announcement-early-finish", ("departTime", departTime)));
    }
    // End Frontier: early expedition end

    private void OnSalvageConsoleInit(Entity<SalvageExpeditionConsoleComponent> console, ref ComponentInit args)
    {
        UpdateConsole(console);
    }

    private void OnSalvageConsoleParent(Entity<SalvageExpeditionConsoleComponent> console, ref EntParentChangedMessage args)
    {
        UpdateConsole(console);
    }

    private void UpdateConsoles(EntityUid stationUid, SalvageExpeditionDataComponent component)
    {
        var state = GetState(component);

        var query = AllEntityQuery<SalvageExpeditionConsoleComponent, UserInterfaceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var uiComp, out var xform))
        {
            var station = _station.GetOwningStation(uid, xform);

            if (station != stationUid)
                continue;

            // Frontier: if we have a lingering FTL component, we cannot start a new mission
            if (!TryComp<StationDataComponent>(station, out var stationData) ||
                    _station.GetLargestGrid(stationData) is not { Valid: true } grid ||
                    HasComp<FTLComponent>(grid))
            {
                state.Cooldown = true; //Hack: disable buttons
            }
            // End Frontier

            _ui.SetUiState((uid, uiComp), SalvageConsoleUiKey.Expedition, state);
        }
    }

    private void UpdateConsole(Entity<SalvageExpeditionConsoleComponent> component)
    {
        var station = _station.GetOwningStation(component);
        SalvageExpeditionConsoleState state;

        if (TryComp<SalvageExpeditionDataComponent>(station, out var dataComponent))
        {
            state = GetState(dataComponent);
        }
        else
        {
            state = new SalvageExpeditionConsoleState(TimeSpan.Zero, false, true, false, 0, new List<SalvageMissionParams>()); // Frontier: add false as 4th param
        }

        // Frontier: if we have a lingering FTL component, we cannot start a new mission
        if (!TryComp<StationDataComponent>(station, out var stationData) ||
                _station.GetLargestGrid(stationData) is not { Valid: true } grid ||
                HasComp<FTLComponent>(grid))
        {
            state.Cooldown = true; //Hack: disable buttons
        }
        // End Frontier

        _ui.SetUiState(component.Owner, SalvageConsoleUiKey.Expedition, state);
    }

    private void PlayDenySound(EntityUid uid, SalvageExpeditionConsoleComponent component)
    {
        _audio.PlayPvs(_audio.GetSound(component.ErrorSound), uid);
    }
}
