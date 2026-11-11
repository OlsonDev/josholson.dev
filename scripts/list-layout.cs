#!/usr/bin/env dotnet
#:package Microsoft.CodeAnalysis.CSharp
#:project ../JoshOlson.Shared
#:property PublishAot=false
// A list is either on the line of its opening token or one item per line, never "wrapped, but with several items on a
// line". Applies to the lists below with two or more items. Allowed: every item starts on the opening line
// (`Foo(a, b, c)`, even if the last item runs on), or every item starts on a line of its own and none on the opening
// line. Reported: `Foo(\n  a, b, c)`, `Foo(a,\n  b)`. Roslynator's RCS0053 catches only half of these, so this checks
// the rest. A list that is the value of a declaration (field, property, local, expression body) must also be one item
// per line, not all on the opening line. Covers argument, parameter, type-argument, type-parameter, attribute, tuple,
// initializer, collection expression, and enum member lists. Run through mise: mise run lists:check
using JoshOlson.Shared.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var options = CSharpParseOptions.Default.WithPreprocessorSymbols("INCLUDE_TESTS");
var files = SourceFiles.Enumerate(["JoshOlson.Api", "JoshOlson.Database", "JoshOlson.Seed", "JoshOlson.Shared", "scripts", ".githooks"], "*.cs");

var violations = 0;
foreach (var path in files) {
  var text = await File.ReadAllTextAsync(path, CancellationToken.None);
  var tree = CSharpSyntaxTree.ParseText(text, options, path, cancellationToken: CancellationToken.None);
  var root = await tree.GetRootAsync(CancellationToken.None);
  foreach (var node in root.DescendantNodes()) {
    var items = ItemsOf(node);
    if (items.Count < 2) continue;
    if (FirstMisplaced(items) is { } misplaced) {
      Report(path, misplaced, "put each item on its own line, or all of them on the opening line");
    } else if (IsDeclaredData(node) && IsOnOneLine(items)) {
      Report(path, items[0], "a declared list of two or more items takes one item per line");
    }
  }
}

Console.WriteLine(FormattableString.Invariant($"{violations} misplaced list item(s)."));
return violations > 0 ? 1 : 0;

void Report(string path, SyntaxNode at, string message) {
  var position = at.GetFirstToken().GetLocation().GetLineSpan().StartLinePosition;
  var location = FormattableString.Invariant($"{path}({position.Line + 1},{position.Character + 1})");
  Console.Error.WriteLine($"{location}: {message}");
  violations++;
}

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

// A list that is the value of a declaration (a field, property, local, or expression-bodied member) is data that
// grows, so a new item should be a one-line diff. A list inside a call or a larger expression is left alone.
static bool IsDeclaredData(SyntaxNode node) {
  if (node is not (CollectionExpressionSyntax or InitializerExpressionSyntax or AnonymousObjectCreationExpressionSyntax)) {
    return false;
  }
  // `new Foo { ... }` and `new[] { ... }` are the value; the braces alone are not.
  var isBraceOfCreation = node is InitializerExpressionSyntax {
    Parent: BaseObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax,
  };
  var value = isBraceOfCreation ? node.Parent : node;
  return value?.Parent is EqualsValueClauseSyntax or ArrowExpressionClauseSyntax;
}

static bool IsOnOneLine(IReadOnlyList<SyntaxNode> items)
  => items.Select(item => LineOf(item.GetFirstToken())).Distinct().Count() is 1;
