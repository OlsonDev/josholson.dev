#r "nuget: EmojiToolkit, 16.0.0"

using System.Globalization;
using EmojiToolkit;

// Requirements:
// - Must start with 1-2 emojis (no space between them)
// - A single space
// - A meaningful message (at least 5 characters)
// - No emojis in the message
// - No leading or trailing whitespace
static (bool Success, string Error) ValidateCommitMessage(string commitMsg) {
  var corrections = new List<string>();

  if (commitMsg.Length > 0 && char.IsWhiteSpace(commitMsg[0]))
    corrections.Add("remove all leading whitespace");

  // Count emojis at the start (handles surrogate pairs and combining characters)
  var emojiCount = 0;
  var emojiCharLength = 0;

  var trimmedStart = commitMsg.TrimStart();
  var textElements = StringInfo.GetTextElementEnumerator(trimmedStart);
  while (textElements.MoveNext()) {
    var element = textElements.GetTextElement();
    // Strip variation selectors for checking
    var baseElement = element.Replace("\uFE0E", "").Replace("\uFE0F", "");
    if (string.IsNullOrEmpty(baseElement)) {
      emojiCharLength += element.Length;
      continue;
    }

    if (Emoji.Get(baseElement) is null) break;
    emojiCount++;
    emojiCharLength += element.Length;
  }

  if (emojiCount == 0) {
    corrections.Add("start with 1-2 emojis (see https://gitmoji.dev)");
  } else {
    var emojiPlural = emojiCount == 1 ? "emoji" : "emojis";

    if (emojiCount > 2) corrections.Add("use at most 2 emojis");

    // Check space after emoji(s)
    var afterEmoji = trimmedStart.Substring(emojiCharLength);

    if (afterEmoji.Length == 0) {
      corrections.Add($"add a space after your {emojiPlural}");
      corrections.Add("add a meaningful message (at least 5 characters)");
    } else if (afterEmoji[0] != ' ') {
      corrections.Add($"add a space after your {emojiPlural}");
    } else if (afterEmoji.Length > 1 && afterEmoji[1] == ' ') {
      corrections.Add($"use only one space after your {emojiPlural}");
    } else {
      // Check the message
      var message = afterEmoji.Substring(1);
      if (string.IsNullOrWhiteSpace(message) || message.Trim().Length < 5) {
        corrections.Add("add a meaningful message (at least 5 characters)");
      } else {
        // Check for emojis in the message (not allowed)
        var demojified = Emoji.Demojify(message);
        if (message != demojified)
          corrections.Add("remove emojis from your message (emojis are only allowed at the start)");
      }
    }
  }

  // Check for trailing whitespace
  if (commitMsg.Length > 0 && char.IsWhiteSpace(commitMsg[commitMsg.Length - 1]))
    corrections.Add("remove all trailing whitespace");

  return corrections.Count == 0
    ? (true, null)
    : (false, $"Invalid commit message format. Please {ToFriendlyList(corrections)}.");
}

static string ToFriendlyList(List<string> items, string conjunction = "and") {
  if (items.Count == 0) return "";
  if (items.Count == 1) return items[0];
  if (items.Count == 2) return $"{items[0]} {conjunction} {items[1]}";

  var allButLast = new List<string>();
  for (var i = 0; i < items.Count - 1; i++)
    allButLast.Add(items[i]);

  return $"{string.Join(", ", allButLast)}, {conjunction} {items[items.Count - 1]}";
}