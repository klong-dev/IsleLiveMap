namespace TheIsleOverlay.Core;

/// <summary>
/// Canonical The Isle Evrima playable roster: normalized species id → display
/// abbreviation + diet. Used by the map renderer (marker label + category)
/// and anywhere a raw UClass name (e.g. "BP_Triceratops_C") needs a friendly,
/// stable identity. Unknown species fall back to a truncated raw name and
/// <see cref="CreatureDiet.Unknown"/>.
/// </summary>
public static class EvrimaSpeciesCatalog
{
    public sealed record Entry(string ShortName, CreatureDiet Diet);

    // Key = normalized UClass species word (lowercase, no BP_/_C decorations).
    public static readonly IReadOnlyDictionary<string, Entry> Species =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- carnivores ------------------------------------------------
            ["tyrannosaurus"] = new("TRex", CreatureDiet.Carnivore),
            ["allosaurus"] = new("Allo", CreatureDiet.Carnivore),
            ["carnotaurus"] = new("Carno", CreatureDiet.Carnivore),
            ["ceratosaurus"] = new("Cerato", CreatureDiet.Carnivore),
            ["dilophosaurus"] = new("Dilo", CreatureDiet.Carnivore),
            ["herrerasaurus"] = new("Herrera", CreatureDiet.Carnivore),
            ["omniraptor"] = new("Omni", CreatureDiet.Carnivore),
            ["utahraptor"] = new("Omni", CreatureDiet.Carnivore),
            ["deinosuchus"] = new("Deino", CreatureDiet.Carnivore),
            ["suchomimus"] = new("Sucho", CreatureDiet.Carnivore),
            ["megalania"] = new("Mega", CreatureDiet.Carnivore),
            ["pterosaurs"] = new("Ptero", CreatureDiet.Carnivore),
            ["pterosaur"] = new("Ptero", CreatureDiet.Carnivore),
            ["quetzalcoatlus"] = new("Quetz", CreatureDiet.Carnivore),
            // ---- herbivores ------------------------------------------------
            ["triceratops"] = new("Trike", CreatureDiet.Herbivore),
            ["stegosaurus"] = new("Stego", CreatureDiet.Herbivore),
            ["maiasaura"] = new("Maia", CreatureDiet.Herbivore),
            ["hypsilophodon"] = new("Hypsi", CreatureDiet.Herbivore),
            ["hypsilophon"] = new("Hypsi", CreatureDiet.Herbivore),
            ["tenontosaurus"] = new("Tenonto", CreatureDiet.Herbivore),
            ["therizinosaurus"] = new("Theri", CreatureDiet.Herbivore),
            ["pachycephalosaurus"] = new("Pachy", CreatureDiet.Herbivore),
            ["pachycephalo"] = new("Pachy", CreatureDiet.Herbivore),
            ["gallimimus"] = new("Galli", CreatureDiet.Herbivore),
            ["beipiaosaurus"] = new("Beipi", CreatureDiet.Herbivore),
            ["beipio"] = new("Beipi", CreatureDiet.Herbivore),
            ["diabloceratops"] = new("Diablo", CreatureDiet.Herbivore),
            ["iguanodon"] = new("Iguana", CreatureDiet.Herbivore),
            ["lambeosaurus"] = new("Lambeo", CreatureDiet.Herbivore),
            ["dryosaurus"] = new("Dryo", CreatureDiet.Herbivore),
            ["camarasaurus"] = new("Cama", CreatureDiet.Herbivore),
            ["struthiomimus"] = new("Struthi", CreatureDiet.Herbivore),
            ["hetrerodontosaurus"] = new("Hetero", CreatureDiet.Herbivore),
            ["heterodontosaurus"] = new("Hetero", CreatureDiet.Herbivore),
        };

    /// <summary>
    /// Resolve a raw UClass/species token into a short display name.
    /// Strips BP_/ABP_/ATI prefixes and the _C suffix, then matches the
    /// catalog; unknown species fall back to the first 6 letters upper-cased.
    /// </summary>
    public static string ShortName(string? rawSpecies)
    {
        var norm = CreatureSpeciesIdentity.Normalize(rawSpecies);
        if (string.IsNullOrWhiteSpace(norm))
        {
            return "?";
        }
        if (Species.TryGetValue(norm, out var hit))
        {
            return hit.ShortName;
        }
        // Fallback: compress the raw word (e.g. "spinosaurus" → "SPINOS").
        var word = norm.Length > 6 ? norm[..6] : norm;
        return word.ToUpperInvariant();
    }

    /// <summary>
    /// Diet for a raw species token; Unknown when not in the catalog.
    /// </summary>
    public static CreatureDiet DietOf(string? rawSpecies)
    {
        var norm = CreatureSpeciesIdentity.Normalize(rawSpecies);
        return Species.TryGetValue(norm ?? string.Empty, out var hit)
            ? hit.Diet
            : CreatureDiet.Unknown;
    }
}
