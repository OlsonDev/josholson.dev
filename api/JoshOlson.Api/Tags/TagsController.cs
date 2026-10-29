using Microsoft.AspNetCore.Mvc;

namespace JoshOlson.Api.Tags;

[ApiController]
[Route("tags")]
public sealed class TagsController : ControllerBase {
  private readonly TagService _tagService;

  public TagsController(TagService tagService)
    => _tagService = tagService;

  [HttpGet]
  public async Task<IReadOnlyList<TagResponse>> GetAll(CancellationToken cancellationToken)
    => await _tagService.GetAllAsync(cancellationToken);

  [HttpGet("{tagId:int}")]
  public async Task<ActionResult<TagResponse>> Get(int tagId, CancellationToken cancellationToken) {
    var tag = await _tagService.GetAsync(tagId, cancellationToken);
    return tag is null ? NotFound() : tag;
  }

  [HttpPost]
  public async Task<IActionResult> Create(TagRequest request, CancellationToken cancellationToken) {
    var errors = await _tagService.ValidateCreateAsync(request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    var tagId = await _tagService.CreateAsync(request, cancellationToken);
    return CreatedAtAction(nameof(Get), new { tagId }, null);
  }

  [HttpPut("{tagId:int}")]
  public async Task<IActionResult> Update(int tagId, TagRequest request, CancellationToken cancellationToken) {
    var errors = await _tagService.ValidateUpdateAsync(tagId, request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    return await _tagService.UpdateAsync(tagId, request, cancellationToken) ? NoContent() : NotFound();
  }

  [HttpDelete("{tagId:int}")]
  public async Task<IActionResult> Delete(int tagId, CancellationToken cancellationToken)
    => await _tagService.DeleteAsync(tagId, cancellationToken) ? NoContent() : NotFound();
}
