using IdKeeper.Common.Constants;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public sealed class SnowflakeLayoutRepository(IConnectionMultiplexer multiplexer)
{
	private IDatabase Db => multiplexer.GetDatabase();

	public async Task<SnowflakeLayout> GetAsync(CancellationToken cancellationToken = default)
	{
		HashEntry[] entries = await Db.HashGetAllAsync(RedisKeyNames.SnowflakeLayout.Settings);
		if (entries.Length == 0)
		{
			return SnowflakeConstant.Default;
		}

		Dictionary<string, string> fields = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
		SnowflakeLayout layout = SnowflakeConstant.Default;
		if (fields.TryGetValue("BitCountOfTimestamp", out string? timestamp)
			&& Int32.TryParse(timestamp, out Int32 parsedTimestamp))
		{
			layout = layout with { BitCountOfTimestamp = parsedTimestamp };
		}
		if (fields.TryGetValue("BitCountOfNodeId", out string? nodeId)
			&& Int32.TryParse(nodeId, out Int32 parsedNodeId))
		{
			layout = layout with { BitCountOfNodeId = parsedNodeId };
		}
		if (fields.TryGetValue("BitCountOfSequenceId", out string? sequenceId)
			&& Int32.TryParse(sequenceId, out Int32 parsedSequenceId))
		{
			layout = layout with { BitCountOfSequenceId = parsedSequenceId };
		}
		if (fields.TryGetValue("BaseDateTimeStartYear", out string? startYear)
			&& Int32.TryParse(startYear, out Int32 parsedStartYear))
		{
			layout = layout with { BaseDateTimeStartYear = parsedStartYear };
		}
		return layout;
	}

	public async Task SetAsync(SnowflakeLayout layout, CancellationToken cancellationToken = default)
	{
		layout.EnsureValid();

		await Db.HashSetAsync(RedisKeyNames.SnowflakeLayout.Settings,
		[
			new("BitCountOfTimestamp", layout.BitCountOfTimestamp),
			new("BitCountOfNodeId", layout.BitCountOfNodeId),
			new("BitCountOfSequenceId", layout.BitCountOfSequenceId),
			new("BaseDateTimeStartYear", layout.BaseDateTimeStartYear),
		]);
	}
}
