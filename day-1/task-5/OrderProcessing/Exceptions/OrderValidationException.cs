namespace OrderProcessing.Exceptions;

public sealed class OrderValidationException(string message) : Exception(message);
