namespace JoshOlson.Database.Baking;

// A template database by name, and whether the call that asked for it did the work of building it.
public sealed record BuiltTemplate(string Name, bool Built);
