using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Banandium;

public sealed record UpdateResult(string Message, bool Available = false);

public static class Updates
{
    private sealed record Release(string? tag_name);

    public static readonly string CurrentTag = typeof(Updates).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    public const string ReleasePage = "https://github.com/JakeButf/Banandium/releases/latest";
    private const string ReleaseApi = "https://api.github.com/repos/JakeButf/Banandium/releases/latest";

    private static Version? ParseTag(string? tag)
    {
        string? number = tag?.Trim();
        if (number != null && number.StartsWith("release-", StringComparison.OrdinalIgnoreCase))
            number = number.Substring("release-".Length);
        number = number?.TrimStart('v', 'V').Split('+')[0];
        if (!Version.TryParse(number, out Version? version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    public static async Task<UpdateResult> CheckAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Banandium/" + CurrentTag.TrimStart('v', 'V'));
            using HttpResponseMessage response = await client.GetAsync(ReleaseApi);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateResult("No published release yet");
            }
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            Release? release = JsonSerializer.Deserialize<Release>(json);
            Version? latest = ParseTag(release?.tag_name);
            if (latest == null) return new UpdateResult("Could not read the release version");
            if (latest > ParseTag(CurrentTag))
            {
                return new UpdateResult("Update available: " + release!.tag_name, true);
            }
            return new UpdateResult("You are up to date");
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException)
        {
            return new UpdateResult("Could not check for updates");
        }
    }
}
