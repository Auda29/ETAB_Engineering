namespace ETAB.Engineering.Service;

public sealed class EditorRequestException : Exception
{
    public EditorRequestException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
