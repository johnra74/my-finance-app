namespace MyFinance.Import.Mny.Jet;

/// <summary>Raised when a file is not a readable Jet 4 / MSISAM database.</summary>
public sealed class JetException : Exception
{
    public JetException(string message)
        : base(message)
    {
    }

    public JetException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
