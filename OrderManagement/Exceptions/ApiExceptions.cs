namespace OrderManagement.Exceptions;

public abstract class ApiException : Exception
{
    protected ApiException(string message) : base(message) { }
}

public sealed class NotFoundException : ApiException
{
    public NotFoundException(string message) : base(message) { }
}

public sealed class ConflictException : ApiException
{
    public ConflictException(string message) : base(message) { }
}

public sealed class UnprocessableEntityException : ApiException
{
    public UnprocessableEntityException(string message) : base(message) { }
}
