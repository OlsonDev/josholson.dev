namespace JoshOlson.Api.Common.Binding;

public static class InputTrimmingRegistration {
  extension(IMvcBuilder builder) {
    // Trims every string that comes in: JSON bodies through a converter, everything else through a model binder.
    public IMvcBuilder AddInputTrimming()
      => builder
        .AddMvcOptions(options => options.ModelBinderProviders.Insert(0, new TrimmingModelBinderProvider()))
        .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new TrimmingStringJsonConverter()));
  }
}
