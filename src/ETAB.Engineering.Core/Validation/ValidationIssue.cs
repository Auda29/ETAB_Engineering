namespace ETAB.Engineering.Core.Validation;

public sealed record ValidationIssue(string Code, string Path, string Message);
