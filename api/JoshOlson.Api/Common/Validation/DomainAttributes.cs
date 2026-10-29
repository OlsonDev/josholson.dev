using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace JoshOlson.Api.Common.Validation;

// Each attribute mirrors a database domain (docs/schema/domains.md), so a client hears what is wrong with a field
// before the database check rejects the row. Null passes (add [Required] for that), and an empty string fails,
// which RegularExpressionAttribute would let through. `\z`, not `$`, because `$` also matches before a final newline.
public abstract class DomainPatternAttribute : ValidationAttribute {
  private readonly Regex _regex;

  protected DomainPatternAttribute(string pattern, string errorMessage) : base(errorMessage)
    => _regex = new(pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

  public override bool IsValid(object? value)
    => value is null || (value is string text && _regex.IsMatch(text));
}

// text.trimmed
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class TrimmedAttribute : DomainPatternAttribute {
  public TrimmedAttribute()
    : base(@"^\S(.*\S)?\z", "{0} must not be empty or contain leading/trailing whitespace.") { }
}

// text.kebab_case
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class KebabCaseAttribute : DomainPatternAttribute {
  public KebabCaseAttribute()
    : base(@"^[a-z0-9]+(-[a-z0-9]+)*\z", "{0} must be lowercase words joined by single hyphens, such as 'foo-bar-42'.") { }
}

// color.hex
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class HexColorAttribute : DomainPatternAttribute {
  public HexColorAttribute()
    : base(@"^#[0-9a-f]{6}\z", "{0} must be a lowercase hex color such as '#a1b2c3'.") { }
}
