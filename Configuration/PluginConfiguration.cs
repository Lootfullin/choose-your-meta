using MediaBrowser.Model.Plugins;

namespace RussianMetadata.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string TmdbApiKey { get; set; } = "";
    public bool EnableRussianTitles { get; set; } = true;
    public bool EnableRussianOverviews { get; set; } = true;
    public bool EnableRussianTaglines { get; set; } = true;
    public bool EnableRussianGenres { get; set; } = true;
    public bool EnableRussianStudios { get; set; } = true;
    public bool EnableRussianPeople { get; set; } = true;
    public string ProxyUrl { get; set; } = "";
    public string ProxyUsername { get; set; } = "";
    public string ProxyPassword { get; set; } = "";
}
