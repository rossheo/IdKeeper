using IdKeeper.Common.Constants;
using SimpleBase;
using System.Security.Cryptography;

namespace IdKeeper.Web.Settings;

public static class XApiKeyGenerator
{
	public static string GetIdKeeperApiKey()
		=> $"{XApiKeyConstant.XApiKeyPrefix}{Base58.Bitcoin.Encode(RandomNumberGenerator.GetBytes(32))}";
}
