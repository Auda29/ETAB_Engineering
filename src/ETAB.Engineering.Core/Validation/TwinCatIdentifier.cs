namespace ETAB.Engineering.Core.Validation;

internal static class TwinCatIdentifier
{
    private static readonly HashSet<string> ReservedWords = new(
        [
            "ACTION", "AND", "AND_THEN", "ARRAY", "AT",
            "BOOL", "BYTE", "BY",
            "CASE", "CHAR", "CONSTANT", "CONTINUE",
            "DATE", "DATE_AND_TIME", "DINT", "DO", "DWORD",
            "ELSE", "ELSIF", "END_ACTION", "END_CASE", "END_FOR", "END_FUNCTION",
            "END_FUNCTION_BLOCK", "END_IF", "END_INTERFACE", "END_METHOD", "END_PROGRAM",
            "END_PROPERTY", "END_REPEAT", "END_STRUCT", "END_TYPE", "END_UNION", "END_VAR",
            "END_WHILE", "EXIT", "EXTENDS",
            "FALSE", "F_EDGE", "FINAL", "FOR", "FUNCTION", "FUNCTION_BLOCK",
            "IF", "IMPLEMENTS", "IN", "INT", "INTERFACE", "INTERNAL",
            "LDATE", "LDATE_AND_TIME", "LDT", "LINT", "LREAL", "LTIME", "LTIME_OF_DAY",
            "LTOD", "LWORD",
            "METHOD", "MOD",
            "NON_RETAIN", "NOT", "NULL",
            "OF", "OR", "OR_ELSE", "OVERRIDE",
            "PERSISTENT", "POINTER", "PRIVATE", "PROGRAM", "PROPERTY", "PROTECTED", "PUBLIC",
            "R_EDGE", "REAL", "REFERENCE", "REPEAT", "RETAIN", "RETURN",
            "SINT", "STRING", "STRUCT", "SUPER",
            "THEN", "THIS", "TIME", "TIME_OF_DAY", "TO", "TOD", "TRUE", "TYPE",
            "UDINT", "UINT", "ULINT", "UNION", "UNTIL", "USINT",
            "VAR", "VAR_ACCESS", "VAR_CONFIG", "VAR_EXTERNAL", "VAR_GLOBAL", "VAR_IN_OUT",
            "VAR_INPUT", "VAR_INST", "VAR_OUTPUT", "VAR_STAT", "VAR_TEMP",
            "WCHAR", "WHILE", "WORD", "WSTRING",
            "XOR"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsReserved(string value) => ReservedWords.Contains(value);
}
