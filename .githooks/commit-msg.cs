#load "commit-msg-validator.csx"

// Skip validation during merge
if (File.Exists(".git/MERGE_HEAD")) return 0;

var commitMsgPath = Args[0];
// Remove one trailing newline character because most (all?) git clients add one
var commitMsg = File.ReadAllText(commitMsgPath, Encoding.UTF8).TrimEnd('\n');
var (success, error) = ValidateCommitMessage(commitMsg);
if (success) return 0;
Console.Error.WriteLine(error);
return 1;