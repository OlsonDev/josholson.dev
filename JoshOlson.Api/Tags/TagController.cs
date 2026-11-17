using JoshOlson.Api.Common.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JoshOlson.Api.Tags;

/// <summary>The tags that group things on the site. Anyone can read them; changing them needs a session.</summary>
[ApiController]
[Route("tags")]
public sealed class TagController : ControllerBase {
  private readonly TagService _tagService;

  /// <summary>Takes the service the actions call.</summary>
  public TagController(TagService tagService)
    => _tagService = tagService;

  /// <summary>Lists every tag.</summary>
  [AllowAnonymous]
  [HttpGet]
  public async Task<List<TagResponse>> GetAll(CancellationToken cancellationToken)
    => await _tagService.GetAllAsync(cancellationToken);

  /// <summary>Gets one tag.</summary>
  /// <param name="tagId">The ID of the tag.</param>
  /// <response code="200">The tag.</response>
  /// <response code="404">There is no tag with that ID.</response>
  [AllowAnonymous]
  [HttpGet("{tagId:int}")]
  [ProducesResponseType<TagResponse>(StatusCodes.Status200OK)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<TagResponse>> Get(int tagId, CancellationToken cancellationToken) {
    var tag = await _tagService.GetAsync(tagId, cancellationToken);
    return tag is null ? NotFound() : tag;
  }

  /// <summary>Creates a tag.</summary>
  /// <param name="request">The tag to create.</param>
  /// <response code="201">Created. The Location header is the new tag's URL, and there is no body.</response>
  /// <response code="422">A field is not valid, or the name or slug is taken. The errors name each field.</response>
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status201Created)]
  [ValidationProblemResponse]
  public async Task<IActionResult> Create(TagRequest request, CancellationToken cancellationToken) {
    var errors = await _tagService.ValidateCreateAsync(request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    var tagId = await _tagService.CreateAsync(request, cancellationToken);
    return CreatedAtAction(nameof(Get), new { tagId }, value: null);
  }

  /// <summary>Replaces a tag's fields.</summary>
  /// <param name="tagId">The ID of the tag.</param>
  /// <param name="request">What the tag becomes.</param>
  /// <response code="204">Replaced.</response>
  /// <response code="404">There is no tag with that ID.</response>
  /// <response code="422">A field is not valid, or the name or slug is taken. The errors name each field.</response>
  [HttpPut("{tagId:int}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  [ValidationProblemResponse]
  public async Task<IActionResult> Update(int tagId, TagRequest request, CancellationToken cancellationToken) {
    var errors = await _tagService.ValidateUpdateAsync(tagId, request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    return await _tagService.UpdateAsync(tagId, request, cancellationToken) ? NoContent() : NotFound();
  }

  /// <summary>Deletes a tag.</summary>
  /// <param name="tagId">The ID of the tag.</param>
  /// <response code="204">Deleted.</response>
  /// <response code="404">There is no tag with that ID.</response>
  [HttpDelete("{tagId:int}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> Delete(int tagId, CancellationToken cancellationToken)
    => await _tagService.DeleteAsync(tagId, cancellationToken) ? NoContent() : NotFound();
}
