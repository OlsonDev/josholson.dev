using Microsoft.AspNetCore.Mvc;

namespace JoshOlson.Api.Tags;

[ApiController]
[Route("tags")]
public sealed class TagsController : ControllerBase {
  private readonly TagService _service;

  public TagsController(TagService service)
    => _service = service;

  [HttpGet]
  public async Task<IReadOnlyList<TagResponse>> GetAll(CancellationToken cancellationToken)
    => await _service.GetAllAsync(cancellationToken);

  [HttpGet("{tagId:int}")]
  public async Task<ActionResult<TagResponse>> Get(int tagId, CancellationToken cancellationToken) {
    var tag = await _service.GetAsync(tagId, cancellationToken);
    return tag is null ? NotFound() : tag;
  }

  [HttpPost]
  public async Task<IActionResult> Create(TagRequest request, CancellationToken cancellationToken) {
    var tagId = await _service.CreateAsync(request, cancellationToken);
    return CreatedAtAction(nameof(Get), new { tagId }, null);
  }

  [HttpPut("{tagId:int}")]
  public async Task<IActionResult> Update(int tagId, TagRequest request, CancellationToken cancellationToken)
    => await _service.UpdateAsync(tagId, request, cancellationToken) ? NoContent() : NotFound();

  [HttpDelete("{tagId:int}")]
  public async Task<IActionResult> Delete(int tagId, CancellationToken cancellationToken)
    => await _service.DeleteAsync(tagId, cancellationToken) ? NoContent() : NotFound();
}
