using JoshOlson.Shared.Extensions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JoshOlson.Api.Common.Binding;

// The route, query, form, and header counterpart of `TrimmingStringJsonConverter`.
public sealed class TrimmingModelBinder : IModelBinder {
  public Task BindModelAsync(ModelBindingContext bindingContext) {
    var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
    if (result == ValueProviderResult.None) return Task.CompletedTask;

    bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
    bindingContext.Result = ModelBindingResult.Success(result.FirstValue.TrimToNull());
    return Task.CompletedTask;
  }
}
