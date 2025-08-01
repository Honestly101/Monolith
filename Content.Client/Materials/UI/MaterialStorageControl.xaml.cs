// SPDX-FileCopyrightText: 2024 Deserty0
// SPDX-FileCopyrightText: 2024 Nemanja
// SPDX-FileCopyrightText: 2024 eoineoineoin
// SPDX-FileCopyrightText: 2025 cheetah1984
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Materials.UI;

/// <summary>
/// This widget is one row in the lathe eject menu.
/// </summary>
[GenerateTypedNameReferences]
public sealed partial class MaterialStorageControl : ScrollContainer
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    private readonly MaterialStorageSystem _materialStorage;

    private EntityUid? _owner;

    private Dictionary<ProtoId<MaterialPrototype>, int> _currentMaterials = new();

    public MaterialStorageControl()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);

        _materialStorage = _entityManager.System<MaterialStorageSystem>();
    }

    public void SetOwner(EntityUid owner)
    {
        _owner = owner;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_owner == null)
            return;

        if (_entityManager.Deleted(_owner) || !_entityManager.TryGetComponent<MaterialStorageComponent>(_owner, out var materialStorage))
        {
            _owner = null;
            return;
        }

        var canEject = materialStorage.CanEjectStoredMaterials;
        var mats = _materialStorage.GetStoredMaterials((_owner.Value, materialStorage));

        if (_currentMaterials.Equals(mats))
            return;

        var missing = new List<string>();
        var extra = new List<string>();
        foreach (var (mat, amount) in mats)
        {
            if (!_currentMaterials.ContainsKey(mat) ||
                _currentMaterials[mat] == 0 && _currentMaterials[mat] != amount)
                missing.Add(mat);
        }
        foreach (var (mat, amount) in _currentMaterials)
        {
            if (!mats.ContainsKey(mat) || amount == 0)
                extra.Add(mat);
        }

        var children = new List<MaterialDisplay>();
        children.AddRange(MaterialList.Children.OfType<MaterialDisplay>());

        foreach (var display in children)
        {
            var mat = display.Material;

            if (extra.Contains(mat))
            {
                MaterialList.RemoveChild(display);
                continue;
            }

            if (!mats.TryGetValue(mat, out var newAmount))
                continue;
            display.UpdateVolume(newAmount);
        }

        foreach (var mat in missing)
        {
            var volume = mats[mat];
            MaterialList.AddChild(new MaterialDisplay(_owner.Value, mat, volume, canEject));
        }

        _currentMaterials = mats;
        NoMatsLabel.Visible = MaterialList.ChildCount == 1;
        SiloLinkedLabel.Visible = _entityManager.TryGetComponent<OreSiloClientComponent>(_owner.Value, out var client) && client.Silo != null;
    }
}
