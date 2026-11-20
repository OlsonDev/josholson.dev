using JoshOlson.Api.Common.Contract;
using JoshOlson.Api.Common.Deletion;
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

  /// <summary>Gets one tag by a slug it has now or had before.</summary>
  /// <param name="slug">A slug of the tag.</param>
  /// <response code="200">The tag, for its current slug.</response>
  /// <response code="308">The slug is a previous one. The Location header is the tag's URL for its current slug.</response>
  /// <response code="404">No tag has, or ever had, that slug.</response>
  [AllowAnonymous]
  [HttpGet("by-slug/{slug}")]
  [ProducesResponseType<TagResponse>(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status308PermanentRedirect)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<TagResponse>> GetBySlug(string slug, CancellationToken cancellationToken) {
    var result = await _tagService.GetBySlugAsync(slug, cancellationToken);
    return result switch {
      TagResponse tag => tag,
      TagMoved moved => RedirectPermanentPreserveMethod($"/tags/by-slug/{Uri.EscapeDataString(moved.Slug)}"),
      TagNotFound => NotFound(),
    };
  }

  /// <summary>Creates a tag.</summary>
  /// <param name="request">The tag to create.</param>
  /// <response code="201">Created. The Location header is the new tag's URL, and there is no body.</response>
  /// <response code="422">A field is not valid, or the name, slug, or an alias is taken. The errors name each field.</response>
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
  /// <response code="409">The tag was saved by someone else after ExpectedVersion. Read it again.</response>
  /// <response code="422">A field is not valid, or the name, slug, or an alias is taken. The errors name each field.</response>
  [HttpPut("{tagId:int}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  [ProblemResponse(StatusCodes.Status409Conflict)]
  [ValidationProblemResponse]
  public async Task<IActionResult> Update(int tagId, TagRequest request, CancellationToken cancellationToken) {
    var errors = await _tagService.ValidateUpdateAsync(tagId, request, cancellationToken);
    if (errors.Any) return UnprocessableEntity(errors.ToProblemDetails());

    var result = await _tagService.UpdateAsync(tagId, request, cancellationToken);
    return result switch {
      TagUpdated => NoContent(),
      TagNotFound => NotFound(),
      TagChanged => Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "The tag was saved by someone else since it was read."),
    };
  }

  /// <summary>Says what deleting a tag would affect, so the person can decide before they do it.</summary>
  /// <param name="tagId">The ID of the tag.</param>
  /// <response code="200">What refers to the tag: what blocks the delete, and what it would also change.</response>
  /// <response code="404">There is no tag with that ID.</response>
  [HttpGet("{tagId:int}/deletion-impact")]
  [ProducesResponseType<DeletionImpact>(StatusCodes.Status200OK)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<DeletionImpact>> GetDeletionImpact(int tagId, CancellationToken cancellationToken) {
    var impact = await _tagService.GetDeletionImpactAsync(tagId, cancellationToken);
    return impact is null ? NotFound() : impact;
  }

  /// <summary>Deletes a tag, taking it off everything it is on.</summary>
  /// <param name="tagId">The ID of the tag.</param>
  /// <response code="204">Deleted.</response>
  /// <response code="404">There is no tag with that ID.</response>
  /// <response code="409">Something blocks the delete. The body is the same as the deletion impact's, with the blockers.</response>
  [HttpDelete("{tagId:int}")]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProblemResponse(StatusCodes.Status404NotFound)]
  [ProducesResponseType<DeletionImpact>(StatusCodes.Status409Conflict)]
  public async Task<IActionResult> Delete(int tagId, CancellationToken cancellationToken) {
    var result = await _tagService.DeleteAsync(tagId, cancellationToken);
    return result switch {
      TagDeleted => NoContent(),
      TagNotFound => NotFound(),
      TagBlocked blocked => Conflict(blocked.Impact),
    };
  }
}
