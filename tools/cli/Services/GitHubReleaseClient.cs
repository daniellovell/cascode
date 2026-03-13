using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cascode.Cli.Services;

internal sealed record GitHubRelease(string TagName, GitHubReleaseAsset[] Assets);

internal sealed record GitHubReleaseAsset(string Name, string BrowserDownloadUrl);

internal interface IGitHubReleaseClient
{
    GitHubRelease? FetchLatestRelease();
    GitHubRelease? FetchReleaseByTag(string tagName);
}

internal sealed class GitHubReleaseClient : IGitHubReleaseClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private const string ReleasesApiBase =
        "https://api.github.com/repos/daniellovell/cascode/releases";

    public GitHubRelease? FetchLatestRelease() => FetchRelease($"{ReleasesApiBase}/latest");

    public GitHubRelease? FetchReleaseByTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return null;
        return FetchRelease($"{ReleasesApiBase}/tags/{Uri.EscapeDataString(tagName)}");
    }

    private static GitHubRelease? FetchRelease(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "cascode-cli");
        request.Headers.Add("Accept", "application/vnd.github+json");

        using var response = Http.Send(request);
        if (!response.IsSuccessStatusCode)
            return null;

        using var stream = response.Content.ReadAsStream();
        var payload = JsonSerializer.Deserialize<GitHubReleasePayload>(stream);
        if (payload is null)
            return null;

        var assets = payload.Assets ?? Array.Empty<GitHubAssetPayload>();
        var mappedAssets = new GitHubReleaseAsset[assets.Length];
        for (var i = 0; i < assets.Length; i++)
        {
            mappedAssets[i] = new GitHubReleaseAsset(assets[i].Name, assets[i].BrowserDownloadUrl);
        }

        return new GitHubRelease(payload.TagName, mappedAssets);
    }

    private sealed record GitHubReleasePayload
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = "";

        [JsonPropertyName("assets")]
        public GitHubAssetPayload[]? Assets { get; init; }
    }

    private sealed record GitHubAssetPayload
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = "";
    }
}
