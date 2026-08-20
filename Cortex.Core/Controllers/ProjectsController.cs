using Cortex.Core.Auth;
using Cortex.Core.Dtos;
using Cortex.Core.Objects;
using Cortex.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cortex.Core.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly ICurrentUser _me;
    private readonly IProjectService _svc;

    public ProjectsController(ICurrentUser me, IProjectService svc)
    {
        _me = me;
        _svc = svc;
    }

    /// <summary>Flat list (roots + folders); the client builds the 2-level tree.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await _svc.ListAsync(_me.UserId, ct);
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest req, CancellationToken ct)
    {
        try
        {
            var project = await _svc.CreateAsync(_me.UserId, req.Name, req.ParentId, ct);
            return CreatedAtAction(nameof(List), new { id = project.Id }, new ProjectResponse(
                project.Id, project.ParentId, project.Name, 0, project.CreatedAt, project.UpdatedAt));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorDetail(ex.Message));
        }
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProjectRequest req, CancellationToken ct)
    {
        try
        {
            await _svc.RenameAsync(_me.UserId, id, req.Name, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message == "Project not found"
                ? NotFound(new ErrorDetail(ex.Message))
                : BadRequest(new ErrorDetail(ex.Message));
        }
    }

    /// <summary>Deletes folders with the project; conversations are unfiled, never deleted.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _svc.DeleteAsync(_me.UserId, id, ct);
        return ok ? NoContent() : NotFound();
    }
}
