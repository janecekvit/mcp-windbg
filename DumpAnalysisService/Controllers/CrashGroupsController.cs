using DumpAnalysisService.Fingerprinting;
using DumpAnalysisService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Models;

namespace DumpAnalysisService.Controllers;

/// <summary>
/// Crash groups recorded by fingerprint_crash / POST /api/jobs/fingerprint-crash.
/// </summary>
[ApiController]
[Route("api/crash-groups")]
[Produces("application/json")]
[Authorize(Policy = DumpPolicies.Read)]
public class CrashGroupsController : ControllerBase
{
    private readonly ICrashFingerprintService _fingerprints;

    public CrashGroupsController(ICrashFingerprintService fingerprints) => _fingerprints = fingerprints;

    /// <summary>Known crash groups, most recently seen first.</summary>
    [HttpGet]
    [ProducesResponseType<CrashGroupList>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCrashGroups(CancellationToken cancellationToken) =>
        Ok(await _fingerprints.ListGroupsAsync(cancellationToken));
}
