#!/usr/bin/env dotnet
#:package Microsoft.CodeAnalysis.CSharp@5.0.0
// A list is either on the line of its opening token or one item per line, never "wrapped, but with several items on a
// line". Applies to the lists below with two or more items. Allowed: every item starts on the opening line
// (`Foo(a, b, c)`, even if the last item runs on), or every item starts on a line of its own and none on the opening
// line. Reported: `Foo(\n  a, b, c)`, `Foo(a,\n  b)`. Roslynator's RCS0053 catches only half of these, so this checks
// the rest. Covers argument, parameter, type-argument, type-parameter, attribute, tuple, initializer, collection
// expression, and enum member lists. Run through mise: mise run lists:check
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var options = CSharpParseOptions.Default.WithPreprocessorSymbols("INCLUDE_TESTS");
var files = new[] { "api", "scripts", ".githooks" }
  .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
  .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
  .Order(StringComparer.Ordinal);

var violations = 0;
foreach (var path in files) {
  var text = await File.ReadAllTextAsync(path, CancellationToken.None);
  var tree = CSharpSyntaxTree.ParseText(text, options, path, cancellationToken: CancellationToken.None);
  var root = await tree.GetRootAsync(CancellationToken.None);
  foreach (var node in root.DescendantNodes()) {
    var items = ItemsOf(node);
    if (items.Count < 2 || FirstMisplaced(items) is not { } misplaced) continue;
    var position = misplaced.GetFirstToken().GetLocation().GetLineSpan().StartLinePosition;
    var location = FormattableString.Invariant($"{path}({position.Line + 1},{position.Character + 1})");
    Console.Error.WriteLine($"{location}: put each item on its own line, or all of them on the opening line");
    violations++;
  }
}

Console.WriteLine(FormattableString.Invariant($"{violations} misplaced list item(s)."));
return violations > 0 ? 1 : 0;

static IReadOnlyList<SyntaxNode> ItemsOf(SyntaxNode node)
  => node switch {
    ArgumentListSyntax list => list.Arguments,
    BracketedArgumentListSyntax list => list.Arguments,
    ParameterListSyntax list => list.Parameters,
    BracketedParameterListSyntax list => list.Parameters,
    TypeArgumentListSyntax list => list.Arguments,
    TypeParameterListSyntax list => list.Parameters,
    AttributeArgumentListSyntax list => list.Arguments,
    TupleExpressionSyntax list => list.Arguments,
    TupleTypeSyntax list => list.Elements,
    InitializerExpressionSyntax list => list.Expressions,
    AnonymousObjectCreationExpressionSyntax list => list.Initializers,
    CollectionExpressionSyntax list => list.Elements,
    EnumDeclarationSyntax list => list.Members,
    _ => [],
  };

// The first item that breaks the rule, or null when the list is fine.
static SyntaxNode? FirstMisplaced(IReadOnlyList<SyntaxNode> items) {
  var openLine = LineOf(items[0].GetFirstToken().GetPreviousToken());
  var lines = items.Select(item => LineOf(item.GetFirstToken())).ToList();
  if (lines.All(line => line == openLine)) return null;
  for (var index = 0; index < items.Count; index++) {
    if (lines[index] == openLine || index > 0 && lines[index] == lines[index - 1]) return items[index];
  }
  return null;
}

static int LineOf(SyntaxToken token)
  => token.GetLocation().GetLineSpan().StartLinePosition.Line;
