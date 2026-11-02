using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JoshOlson.Api.Common.Binding;

// Offers `TrimmingModelBinder` for a string that comes from the URL, a form, or a header. A body is read by the JSON
// input formatter instead, so it is left to `TrimmingStringJsonConverter`. Registered ahead of MVC's own providers.
public sealed class TrimmingModelBinderProvider : IModelBinderProvider {
  // `BindingSource` is a class with static fields, not an enum, so a pattern cannot name its members (`is` needs
  // constants). `Contains` compares them with `Equals`.
  private static readonly BindingSource[] _TextSources = [
    BindingSource.Path,
    BindingSource.Query,
    BindingSource.Form,
    BindingSource.Header,
    BindingSource.ModelBinding,
  ];

  private readonly TrimmingModelBinder _binder = new();

  public IModelBinder? GetBinder(ModelBinderProviderContext context)
    => context.Metadata.ModelType == typeof(string) && IsFromTheRequestLine(context.BindingInfo.BindingSource)
      ? _binder
      : null;

  // No declared source means MVC will infer one from the route or query.
  private static bool IsFromTheRequestLine(BindingSource? source)
    => source is null || _TextSources.Contains(source);
}
