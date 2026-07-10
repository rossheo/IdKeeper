using System.Net;

namespace IdKeeper.ApiService.Caching;

public sealed class CidrCache
{
	private volatile IPNetwork[] _networks = [];
	private volatile bool _loaded;

	public IPNetwork[] Networks => _networks;
	public bool IsLoaded => _loaded;

	public void Update(IPNetwork[] networks)
	{
		_networks = networks;
		_loaded = true;
	}
}
