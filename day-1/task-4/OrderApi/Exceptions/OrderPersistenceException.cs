namespace OrderApi.Exceptions;

public sealed class OrderPersistenceException(string message, Exception innerException)
    : Exception(message, innerException);
