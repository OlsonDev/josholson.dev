#!/usr/bin/env dotnet
#:include commit-msg-validator.cs

using System.Text;

// Skip validation during merge
if (File.Exists(".git/MERGE_HEAD")) return 0;

var commitMsgPath = args[0];
// Remove one trailing newline character because most (all?) git clients add one
var commitMsg = File.ReadAllText(commitMsgPath, Encoding.UTF8).TrimEnd('\n');
var (success, error) = CommitMessageValidator.ValidateCommitMessage(commitMsg);
if (success) return 0;
Console.Error.WriteLine(error);
return 1;