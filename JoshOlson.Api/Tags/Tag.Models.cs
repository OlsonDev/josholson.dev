namespace JoshOlson.Api.Tags;

// Which of a tag's unique fields another tag already has. A field that was not checked is not taken.
public sealed record TakenTagFields(bool IsSlugTaken, bool IsNameTaken);
