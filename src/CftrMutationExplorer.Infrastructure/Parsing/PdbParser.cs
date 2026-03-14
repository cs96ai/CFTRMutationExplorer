using System.Globalization;
using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models;

namespace CftrMutationExplorer.Infrastructure.Parsing;

public class PdbParser : IPdbParser
{
    public async Task<ProteinStructure> ParseAsync(
        string filePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDB file not found: {filePath}");

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true);
        return await ParseFromStreamAsync(stream, Path.GetFileName(filePath), progress, cancellationToken);
    }

    public async Task<ProteinStructure> ParseFromStreamAsync(
        Stream stream,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var structure = new ProteinStructure
        {
            FileName = fileName,
            LoadedAt = DateTime.UtcNow
        };

        var atoms = new List<Atom>();
        var lines = new List<string>();

        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            lines.Add(line);
        }

        int totalLines = lines.Count;
        int processed = 0;

        foreach (var currentLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            processed++;
            if (progress != null && processed % 1000 == 0)
                progress.Report((int)((double)processed / totalLines * 100));

            if (currentLine.Length < 6)
                continue;

            var recordType = currentLine[..6].TrimEnd();

            switch (recordType)
            {
                case "HEADER":
                    structure.Header = SafeSubstring(currentLine, 10, currentLine.Length - 10).Trim();
                    break;

                case "TITLE":
                    var titleText = SafeSubstring(currentLine, 10, currentLine.Length - 10).Trim();
                    structure.Title = string.IsNullOrEmpty(structure.Title)
                        ? titleText
                        : structure.Title + " " + titleText;
                    break;

                case "REMARK":
                    structure.Remarks.Add(SafeSubstring(currentLine, 7, currentLine.Length - 7).Trim());
                    break;

                case "ATOM":
                case "HETATM":
                    var atom = ParseAtomLine(currentLine, recordType == "HETATM");
                    if (atom != null)
                        atoms.Add(atom);
                    break;
            }
        }

        BuildStructure(structure, atoms);
        progress?.Report(100);

        return structure;
    }

    private static Atom? ParseAtomLine(string line, bool isHetAtm)
    {
        try
        {
            if (line.Length < 54)
                return null;

            var atom = new Atom
            {
                IsHetAtom = isHetAtm,
                SerialNumber = ParseInt(SafeSubstring(line, 6, 5)),
                Name = SafeSubstring(line, 12, 4).Trim(),
                AltLoc = line.Length > 16 ? line[16] : ' ',
                ResidueName = SafeSubstring(line, 17, 3).Trim(),
                ChainId = line.Length > 21 ? line[21] : 'A',
                ResidueSequenceNumber = ParseInt(SafeSubstring(line, 22, 4)),
                X = ParseDouble(SafeSubstring(line, 30, 8)),
                Y = ParseDouble(SafeSubstring(line, 38, 8)),
                Z = ParseDouble(SafeSubstring(line, 46, 8))
            };

            if (line.Length >= 60)
                atom.Occupancy = ParseDouble(SafeSubstring(line, 54, 6));

            if (line.Length >= 66)
                atom.TemperatureFactor = ParseDouble(SafeSubstring(line, 60, 6));

            if (line.Length >= 78)
                atom.Element = SafeSubstring(line, 76, 2).Trim();
            else if (!string.IsNullOrEmpty(atom.Name))
                atom.Element = new string(atom.Name.TakeWhile(c => char.IsLetter(c)).ToArray()).Trim();

            return atom;
        }
        catch
        {
            return null;
        }
    }

    private static void BuildStructure(ProteinStructure structure, List<Atom> atoms)
    {
        var chainGroups = atoms.GroupBy(a => a.ChainId).OrderBy(g => g.Key);

        foreach (var chainGroup in chainGroups)
        {
            var chain = new Chain { Id = chainGroup.Key };

            var residueGroups = chainGroup
                .GroupBy(a => (a.ResidueSequenceNumber, a.ResidueName))
                .OrderBy(g => g.Key.ResidueSequenceNumber);

            foreach (var residueGroup in residueGroups)
            {
                var residue = new Residue
                {
                    SequenceNumber = residueGroup.Key.ResidueSequenceNumber,
                    Name = residueGroup.Key.ResidueName,
                    ChainId = chainGroup.Key,
                    Atoms = residueGroup.ToList()
                };
                chain.Residues.Add(residue);
            }

            structure.Chains.Add(chain);
        }
    }

    private static string SafeSubstring(string s, int start, int length)
    {
        if (start >= s.Length) return string.Empty;
        if (start + length > s.Length) length = s.Length - start;
        return s.Substring(start, length);
    }

    private static int ParseInt(string s) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double ParseDouble(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0;
}
