namespace CodeFoxtrot.CloudLocal;

public sealed class CloudLocalNotFoundException : Exception
{
    public CloudLocalNotFoundException(string message) : base(message)
    {
    }

    public CloudLocalNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
