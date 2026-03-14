using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.App.ViewModels;

public enum ViewMode
{
    ReferenceOnly,
    MutantOnly,
    Overlay,
    HighlightMutation
}

public enum ColorScheme
{
    ByChain,
    ByResidueType,
    ByTemperatureFactor,
    SingleColor
}

public partial class ViewportViewModel : ObservableObject
{
    [ObservableProperty]
    private ViewMode _currentViewMode = ViewMode.ReferenceOnly;

    [ObservableProperty]
    private ColorScheme _currentColorScheme = ColorScheme.ByChain;

    [ObservableProperty]
    private double _referenceOpacity = 1.0;

    [ObservableProperty]
    private double _mutantOpacity = 0.7;

    [ObservableProperty]
    private bool _showReferenceStructure = true;

    [ObservableProperty]
    private bool _showMutantStructure;

    [ObservableProperty]
    private bool _highlightResidue508 = true;

    [ObservableProperty]
    private double _atomRadius = 0.4;

    [ObservableProperty]
    private double _bondRadius = 0.2;

    [ObservableProperty]
    private int? _selectedResidueNumber;

    [ObservableProperty]
    private string _viewportInfoText = "No structure loaded";

    [ObservableProperty]
    private Model3DGroup? _sceneModel;

    private ProteinStructure? _referenceStructure;
    private ProteinStructure? _mutantStructure;

    partial void OnCurrentViewModeChanged(ViewMode value)
    {
        switch (value)
        {
            case ViewMode.ReferenceOnly:
                ShowReferenceStructure = true;
                ShowMutantStructure = false;
                break;
            case ViewMode.MutantOnly:
                ShowReferenceStructure = false;
                ShowMutantStructure = true;
                break;
            case ViewMode.Overlay:
                ShowReferenceStructure = true;
                ShowMutantStructure = true;
                break;
            case ViewMode.HighlightMutation:
                ShowReferenceStructure = true;
                ShowMutantStructure = true;
                HighlightResidue508 = true;
                break;
        }
        RebuildScene();
    }

    partial void OnCurrentColorSchemeChanged(ColorScheme value) => RebuildScene();
    partial void OnAtomRadiusChanged(double value) => RebuildScene();
    partial void OnHighlightResidue508Changed(bool value) => RebuildScene();
    partial void OnReferenceOpacityChanged(double value) => RebuildScene();
    partial void OnMutantOpacityChanged(double value) => RebuildScene();

    public void SetReferenceStructure(ProteinStructure? structure)
    {
        _referenceStructure = structure;
        RebuildScene();
    }

    public void SetMutantStructure(ProteinStructure? structure)
    {
        _mutantStructure = structure;
        RebuildScene();
    }

    [RelayCommand]
    private void SetViewMode(string mode)
    {
        if (Enum.TryParse<ViewMode>(mode, out var vm))
            CurrentViewMode = vm;
    }

    [RelayCommand]
    private void FocusOnMutationSite()
    {
        SelectedResidueNumber = 508;
        CurrentViewMode = ViewMode.HighlightMutation;
    }

    private void RebuildScene()
    {
        var group = new Model3DGroup();

        // Ambient light
        group.Children.Add(new AmbientLight(Color.FromRgb(80, 80, 80)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(200, 200, 200), new Vector3D(-1, -1, -1)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(100, 100, 100), new Vector3D(1, 1, 0.5)));

        if (ShowReferenceStructure && _referenceStructure != null)
        {
            BuildStructureModel(group, _referenceStructure, isReference: true);
        }

        if (ShowMutantStructure && _mutantStructure != null)
        {
            BuildStructureModel(group, _mutantStructure, isReference: false);
        }

        SceneModel = group;
        UpdateInfoText();
    }

    private void BuildStructureModel(Model3DGroup group, ProteinStructure structure, bool isReference)
    {
        var opacity = isReference ? ReferenceOpacity : MutantOpacity;

        foreach (var chain in structure.Chains)
        {
            foreach (var residue in chain.Residues)
            {
                var color = GetResidueColor(residue, chain, isReference);

                bool isMutationSite = residue.SequenceNumber == 508 && HighlightResidue508;
                bool isNearMutationSite = Math.Abs(residue.SequenceNumber - 508) <= 5 &&
                                           residue.SequenceNumber != 508 &&
                                           HighlightResidue508 &&
                                           CurrentViewMode == ViewMode.HighlightMutation;

                if (isMutationSite)
                    color = Color.FromRgb(230, 60, 60);
                else if (isNearMutationSite)
                    color = Color.FromRgb(240, 200, 60);

                var radius = isMutationSite ? AtomRadius * 1.3 : AtomRadius;

                var backboneNames = new HashSet<string> { "N", "CA", "C", "O", "CB" };

                foreach (var atom in residue.Atoms)
                {
                    if (!backboneNames.Contains(atom.Name))
                        continue;

                    var elementRadius = atom.Name == "CA" ? radius * 1.2 :
                                        atom.Name == "CB" ? radius * 0.9 :
                                        atom.Name == "O"  ? radius * 0.8 : radius;
                    AddAtomSphere(group, atom, elementRadius, color, opacity);
                }

                var caAtom = residue.Atoms.FirstOrDefault(a => a.Name == "CA");
                var nAtom = residue.Atoms.FirstOrDefault(a => a.Name == "N");
                var cAtom = residue.Atoms.FirstOrDefault(a => a.Name == "C");
                var oAtom = residue.Atoms.FirstOrDefault(a => a.Name == "O");
                var cbAtom = residue.Atoms.FirstOrDefault(a => a.Name == "CB");

                if (nAtom != null && caAtom != null)
                    AddBond(group, nAtom, caAtom, color, opacity);
                if (caAtom != null && cAtom != null)
                    AddBond(group, caAtom, cAtom, color, opacity);
                if (cAtom != null && oAtom != null)
                    AddBond(group, cAtom, oAtom, color, opacity);
                if (caAtom != null && cbAtom != null)
                    AddBond(group, caAtom, cbAtom, color, opacity);

                // For PHE-508 mutation site, also render the aromatic ring atoms
                if (isMutationSite && residue.Name.Trim() == "PHE")
                {
                    var ringNames = new[] { "CG", "CD1", "CD2", "CE1", "CE2", "CZ" };
                    var ringAtoms = ringNames
                        .Select(n => residue.Atoms.FirstOrDefault(a => a.Name == n))
                        .Where(a => a != null)
                        .ToList();

                    foreach (var ra in ringAtoms)
                        AddAtomSphere(group, ra!, AtomRadius * 0.7, color, opacity);

                    // Bond the ring: CG-CD1, CD1-CE1, CE1-CZ, CZ-CE2, CE2-CD2, CD2-CG
                    var ringBondPairs = new[] { ("CG","CD1"), ("CD1","CE1"), ("CE1","CZ"), ("CZ","CE2"), ("CE2","CD2"), ("CD2","CG") };
                    foreach (var (a1Name, a2Name) in ringBondPairs)
                    {
                        var a1 = residue.Atoms.FirstOrDefault(a => a.Name == a1Name);
                        var a2 = residue.Atoms.FirstOrDefault(a => a.Name == a2Name);
                        if (a1 != null && a2 != null)
                            AddBond(group, a1, a2, color, opacity);
                    }

                    // Connect CB to CG
                    var cgAtom = residue.Atoms.FirstOrDefault(a => a.Name == "CG");
                    if (cbAtom != null && cgAtom != null)
                        AddBond(group, cbAtom, cgAtom, color, opacity);
                }
            }

            // Connect consecutive residues (C of residue i to N of residue i+1)
            for (int i = 0; i < chain.Residues.Count - 1; i++)
            {
                var cAtom = chain.Residues[i].Atoms.FirstOrDefault(a => a.Name == "C");
                var nAtom = chain.Residues[i + 1].Atoms.FirstOrDefault(a => a.Name == "N");
                if (cAtom != null && nAtom != null && cAtom.DistanceTo(nAtom) < 6.0)
                {
                    var bondColor = isReference ? Color.FromRgb(100, 180, 100) : Color.FromRgb(200, 140, 80);
                    AddBond(group, cAtom, nAtom, bondColor, opacity);
                }
            }
        }
    }

    private Color GetResidueColor(Residue residue, Chain chain, bool isReference)
    {
        return CurrentColorScheme switch
        {
            ColorScheme.ByChain => GetChainColor(chain.Id, isReference),
            ColorScheme.ByResidueType => GetResidueTypeColor(residue.Name),
            ColorScheme.ByTemperatureFactor => GetTemperatureColor(residue.Atoms.FirstOrDefault()?.TemperatureFactor ?? 20),
            ColorScheme.SingleColor => isReference ? Color.FromRgb(100, 180, 100) : Color.FromRgb(200, 140, 80),
            _ => Colors.Gray
        };
    }

    private static Color GetChainColor(char chainId, bool isReference)
    {
        if (isReference)
        {
            return chainId switch
            {
                'A' => Color.FromRgb(80, 160, 220),
                'B' => Color.FromRgb(100, 200, 100),
                'C' => Color.FromRgb(220, 180, 80),
                'D' => Color.FromRgb(200, 100, 200),
                _ => Color.FromRgb(140, 160, 180)
            };
        }
        return chainId switch
        {
            'A' => Color.FromRgb(220, 120, 60),
            'B' => Color.FromRgb(200, 80, 80),
            'C' => Color.FromRgb(180, 160, 60),
            'D' => Color.FromRgb(160, 100, 180),
            _ => Color.FromRgb(160, 140, 120)
        };
    }

    private static Color GetResidueTypeColor(string residueName)
    {
        var name = residueName.Trim().ToUpperInvariant();
        // Hydrophobic
        if ("ALA VAL LEU ILE PRO PHE MET TRP".Contains(name))
            return Color.FromRgb(255, 200, 50);
        // Polar
        if ("SER THR CYS TYR ASN GLN".Contains(name))
            return Color.FromRgb(100, 200, 100);
        // Positive
        if ("ARG LYS HIS".Contains(name))
            return Color.FromRgb(80, 120, 255);
        // Negative
        if ("ASP GLU".Contains(name))
            return Color.FromRgb(255, 80, 80);
        // Special
        if (name == "GLY")
            return Color.FromRgb(200, 200, 200);

        return Color.FromRgb(180, 180, 180);
    }

    private static Color GetTemperatureColor(double bFactor)
    {
        var normalized = Math.Clamp((bFactor - 5) / 40.0, 0, 1);
        var r = (byte)(normalized * 255);
        var b = (byte)((1 - normalized) * 255);
        return Color.FromRgb(r, 80, b);
    }

    private void AddAtomSphere(Model3DGroup group, Atom atom, double radius, Color color, double opacity)
    {
        var mesh = CreateSphereMesh(new Point3D(atom.X, atom.Y, atom.Z), radius, 8, 8);
        var material = new DiffuseMaterial(new SolidColorBrush(color) { Opacity = opacity });
        var model = new GeometryModel3D(mesh, material);
        model.BackMaterial = material;
        group.Children.Add(model);
    }

    private void AddBond(Model3DGroup group, Atom a1, Atom a2, Color color, double opacity)
    {
        var mesh = CreateCylinderMesh(
            new Point3D(a1.X, a1.Y, a1.Z),
            new Point3D(a2.X, a2.Y, a2.Z),
            BondRadius, 6);
        var material = new DiffuseMaterial(new SolidColorBrush(color) { Opacity = opacity });
        var model = new GeometryModel3D(mesh, material);
        model.BackMaterial = material;
        group.Children.Add(model);
    }

    private static MeshGeometry3D CreateSphereMesh(Point3D center, double radius, int stacks, int slices)
    {
        var mesh = new MeshGeometry3D();
        for (int stack = 0; stack <= stacks; stack++)
        {
            double phi = Math.PI * stack / stacks;
            for (int slice = 0; slice <= slices; slice++)
            {
                double theta = 2 * Math.PI * slice / slices;
                double x = center.X + radius * Math.Sin(phi) * Math.Cos(theta);
                double y = center.Y + radius * Math.Sin(phi) * Math.Sin(theta);
                double z = center.Z + radius * Math.Cos(phi);
                mesh.Positions.Add(new Point3D(x, y, z));
                mesh.Normals.Add(new Vector3D(
                    Math.Sin(phi) * Math.Cos(theta),
                    Math.Sin(phi) * Math.Sin(theta),
                    Math.Cos(phi)));
            }
        }

        for (int stack = 0; stack < stacks; stack++)
        {
            for (int slice = 0; slice < slices; slice++)
            {
                int i0 = stack * (slices + 1) + slice;
                int i1 = i0 + 1;
                int i2 = i0 + (slices + 1);
                int i3 = i2 + 1;

                mesh.TriangleIndices.Add(i0);
                mesh.TriangleIndices.Add(i2);
                mesh.TriangleIndices.Add(i1);

                mesh.TriangleIndices.Add(i1);
                mesh.TriangleIndices.Add(i2);
                mesh.TriangleIndices.Add(i3);
            }
        }

        return mesh;
    }

    private static MeshGeometry3D CreateCylinderMesh(Point3D start, Point3D end, double radius, int sides)
    {
        var mesh = new MeshGeometry3D();
        var direction = end - start;
        var length = direction.Length;
        if (length < 0.001) return mesh;

        direction.Normalize();

        var up = Math.Abs(direction.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
        var right = Vector3D.CrossProduct(direction, up);
        right.Normalize();
        up = Vector3D.CrossProduct(right, direction);
        up.Normalize();

        for (int i = 0; i <= sides; i++)
        {
            double angle = 2 * Math.PI * i / sides;
            var offset = right * (radius * Math.Cos(angle)) + up * (radius * Math.Sin(angle));
            var normal = offset;
            normal.Normalize();

            mesh.Positions.Add(start + offset);
            mesh.Positions.Add(end + offset);
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
        }

        for (int i = 0; i < sides; i++)
        {
            int j = i * 2;
            mesh.TriangleIndices.Add(j);
            mesh.TriangleIndices.Add(j + 2);
            mesh.TriangleIndices.Add(j + 1);

            mesh.TriangleIndices.Add(j + 1);
            mesh.TriangleIndices.Add(j + 2);
            mesh.TriangleIndices.Add(j + 3);
        }

        return mesh;
    }

    private void UpdateInfoText()
    {
        var parts = new List<string>();

        if (_referenceStructure != null && ShowReferenceStructure)
            parts.Add($"Ref: {_referenceStructure.AtomCount:N0} atoms");
        if (_mutantStructure != null && ShowMutantStructure)
            parts.Add($"Mut: {_mutantStructure.AtomCount:N0} atoms");

        parts.Add($"Mode: {CurrentViewMode}");
        parts.Add($"Color: {CurrentColorScheme}");

        ViewportInfoText = parts.Count > 0 ? string.Join(" | ", parts) : "No structure loaded";
    }
}
