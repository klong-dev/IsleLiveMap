namespace TheIsleOverlay.Core;

public static class PrimeQuestVietnamese
{
    private static readonly IReadOnlyDictionary<string, string> Translations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Visit a Sanctuary as a juvenile"] = "Ghé Sanctuary khi còn non",
            ["Get nested in"] = "Được sinh ra từ tổ",
            ["Get perfect diet (1% of each)"] = "Đủ 3 chất dinh dưỡng (mỗi chất ≥ 1%)",
            ["Visit Mass Migration zone"] = "Ghé vùng Đại di cư",
            ["Visit 2 Migration zones"] = "Ghé 2 vùng Di cư",
            ["Visit 4 Patrol zones"] = "Ghé 4 vùng Tuần tra",
            ["Never be Infertile"] = "Không bị Vô sinh",
            ["Never get Muscle spasms"] = "Không bị Co thắt cơ",
            ["Raise children to Subadult"] = "Nuôi con tới Subadult",
            ["Be a Hypsi, Troodon, Beipi, Dryo or Deino"] = "Chơi Hypsi / Troodon / Beipi / Dryo / Deino"
        };

    public static string Translate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Nhiệm vụ chưa xác định";
        }

        var normalized = name.Trim();
        return Translations.TryGetValue(normalized, out var translation)
            ? translation
            : normalized;
    }
}
