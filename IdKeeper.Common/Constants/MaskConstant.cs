namespace IdKeeper.Common.Constants;

public class MaskConstant
{
	public static string MaskApiKey(string? key)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return "-";
		}

		if (key.Length <= 12)
		{
			return new string('*', key.Length);
		}

		return $"{key[..6]}...{key[^6..]}";
	}
}