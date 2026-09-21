using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http;

namespace TheIsleOverlay.App;

internal sealed record HomeReleaseNote(
    string Version,
    string Title,
    string Summary,
    IReadOnlyList<string> Details);

internal sealed class GitHubReleaseNotesService
{
    private const string ReleasesEndpoint =
        "https://api.github.com/repos/klong-dev/IsleLiveMap/releases?per_page=3";

    public async Task<IReadOnlyList<HomeReleaseNote>> LoadAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("IsleLiveMap", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        await using var stream = await client.GetStreamAsync(ReleasesEndpoint, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var notes = new List<HomeReleaseNote>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()
                || release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
            {
                continue;
            }

            var tag = ReadString(release, "tag_name");
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var title = ReadString(release, "name");
            var body = ReadString(release, "body");
            notes.Add(ToNote(tag, title, body));
        }

        return notes;
    }

    private static HomeReleaseNote ToNote(string tag, string title, string body)
    {
        var lines = body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("<!--", StringComparison.Ordinal))
            .Select(line => line.TrimStart('#', '*', '-', ' ', '\t'))
            .Where(line => line.Length > 0)
            .Take(12)
            .ToArray();
        var details = lines.Length == 0 ? ["Cập nhật và tinh chỉnh trải nghiệm Isle Live Map."] : lines;
        var summary = string.Join(" ", details[0].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (summary.Length > 115)
        {
            summary = summary[..112] + "…";
        }

        return new HomeReleaseNote(
            tag.TrimStart('v', 'V'),
            string.IsNullOrWhiteSpace(title) ? "Cập nhật Isle Live Map" : title,
            summary,
            details);
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
