using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RussianMetadata;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("ChooseYourMeta")]
public sealed class ChooseYourMetaController : ControllerBase
{
    private readonly LibraryConfigurationService _configurationService;

    public ChooseYourMetaController(
        LibraryConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    [HttpPost("ConfigureLibraries")]
    public ActionResult<LibraryConfigurationResult> ConfigureLibraries()
    {
        return Ok(_configurationService.Apply());
    }

    [HttpGet("Status")]
    public ActionResult<ChooseYourMetaStatus> GetStatus()
    {
        var configuration =
            Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        return Ok(new ChooseYourMetaStatus(
            !string.IsNullOrWhiteSpace(
                TmdbApiKeyResolver.Resolve(configuration)),
            !string.IsNullOrWhiteSpace(FanartApiKeyResolver.Resolve())));
    }
}

public sealed record ChooseYourMetaStatus(
    bool JellyfinTmdbAvailable,
    bool JellyfinFanartAvailable);
