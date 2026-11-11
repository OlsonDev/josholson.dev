using System.Diagnostics.CodeAnalysis;

// The validation source generator's `IsComplexType` (.NET 11) names DateTime, DateOnly, and the other types
// BannedSymbols.txt bans, in generated code that RS0030 checks anyway. We do not write that code, and the namespace holds
// nothing else. An `.editorconfig` section cannot do this: generator output has no directory in its path to match.
[assembly: SuppressMessage(
  "ApiDesign",
  "RS0030",
  Scope = "namespaceanddescendants",
  Target = "~N:Microsoft.Extensions.Validation.Generated",
  Justification = "Source-generated code that we do not write.")]
