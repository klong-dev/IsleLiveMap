namespace TheIsleOverlay.Core;

public static class CreatureSpeciesIdentity
{
    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tyrannosaurusrex"] = "tyrannosaurus",
            ["trex"] = "tyrannosaurus",
            ["utahraptor"] = "omniraptor",
            ["trike"] = "triceratops",
            ["trice"] = "triceratops"
        };

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var token = value.Trim();
        if (token.StartsWith("Default__", StringComparison.OrdinalIgnoreCase))
        {
            token = token["Default__".Length..];
        }

        if (token.StartsWith("BP_", StringComparison.OrdinalIgnoreCase))
        {
            token = token[3..];
        }
        else if (token.StartsWith("TI", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }

        if (token.EndsWith("_C", StringComparison.OrdinalIgnoreCase))
        {
            token = token[..^2];
        }

        token = new string(token
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return Aliases.TryGetValue(token, out var canonical) ? canonical : token;
    }

    public static bool AreSame(string? left, string? right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        return normalizedLeft is { Length: > 0 }
               && string.Equals(
                   normalizedLeft,
                   normalizedRight,
                   StringComparison.Ordinal);
    }
}
