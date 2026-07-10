namespace IdKeeper.SnowflakeApiService.Exceptions;

// Thrown on critical runtime errors in the renew loop to signal intentional fail-fast shutdown.
public sealed class SnowflakeRuntimeException : Exception
{
	public SnowflakeRuntimeException(string message)
		: base(message)
	{
	}

	public SnowflakeRuntimeException(string message, Exception? innerException)
		: base(message, innerException)
	{
	}
}