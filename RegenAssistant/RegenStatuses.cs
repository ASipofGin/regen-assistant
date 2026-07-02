using System;
using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace RegenAssistant;

public readonly record struct RegenStatusInfo(uint Id, string Name, float Potency);

/// <summary>
/// The set of heal-over-time statuses we know how to estimate. Ids are resolved
/// at load time by name from the Status sheet, so they survive game patches and
/// automatically cover duplicate rows that share a name.
/// </summary>
public sealed class RegenStatuses
{
    /// <summary>HoT effects apply on the actor tick, once every 3 seconds.</summary>
    public const float TickIntervalSeconds = 3f;

    // Cure potency healed per tick. These are the current (7.x) hand-tuned
    // values from the action tooltips; adjust here or override per-id in the
    // config's custom status table.
    private static readonly (string Name, float Potency)[] BuiltinDefinitions =
    [
        // WHM
        ("Regen", 250f),
        ("Medica II", 150f),
        ("Medica III", 175f),
        ("Asylum", 100f),
        // AST
        ("Aspected Benefic", 250f),
        ("Aspected Helios", 150f),
        ("Helios Conjunction", 175f),
        ("Wheel of Fortune", 100f),
        // SCH
        ("Whispering Dawn", 80f),
        ("Angel's Whisper", 80f),
        ("Fey Union", 300f),
        ("Sacred Soil", 100f),
        ("Seraphism", 100f),
        // SGE
        ("Kerakeia", 100f),
        ("Physis", 100f),
        ("Physis II", 130f),
        // GNB
        ("Aurora", 200f),
    ];

    private readonly Dictionary<uint, RegenStatusInfo> byId = [];

    /// <summary>All built-in statuses that resolved to a Status sheet row, by id.</summary>
    public IReadOnlyDictionary<uint, RegenStatusInfo> Builtin => byId;

    public RegenStatuses(IDataManager dataManager, IPluginLog log)
    {
        var byName = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, potency) in BuiltinDefinitions)
            byName[name] = potency;

        // Use the English sheet so matching is independent of the client language.
        foreach (var row in dataManager.GetExcelSheet<Status>(ClientLanguage.English))
        {
            var name = row.Name.ExtractText();
            if (name.Length != 0 && byName.TryGetValue(name, out var potency))
                byId[row.RowId] = new RegenStatusInfo(row.RowId, name, potency);
        }

        log.Information($"Resolved {byId.Count} regen status rows from {BuiltinDefinitions.Length} definitions.");
    }

    /// <summary>
    /// Looks up a status id, honoring the user's custom entries (which win over
    /// built-ins) and their disabled list.
    /// </summary>
    public bool TryGet(uint statusId, Configuration config, out RegenStatusInfo info)
    {
        if (config.CustomStatuses.TryGetValue(statusId, out var customPotency))
        {
            var name = byId.TryGetValue(statusId, out var known) ? known.Name : $"Status #{statusId}";
            info = new RegenStatusInfo(statusId, name, customPotency);
            return true;
        }

        if (!config.DisabledStatuses.Contains(statusId) && byId.TryGetValue(statusId, out info))
            return true;

        info = default;
        return false;
    }
}
