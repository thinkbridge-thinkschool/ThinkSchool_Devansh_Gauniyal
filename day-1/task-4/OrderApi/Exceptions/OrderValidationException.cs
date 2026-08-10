namespace OrderApi.Exceptions;

public sealed class OrderValidationException(string message) : Exception(message);
