-- KEYS[1] = XApiKey ByApiKey 유니크 인덱스 (해당 apiKey 값)
-- KEYS[2] = XApiKey Seq
-- KEYS[3] = XApiKey All Set
-- ARGV[1] = apiKey
-- ARGV[2] = owner
-- ARGV[3] = description
-- ARGV[4] = createdAtUnixSeconds
-- ARGV[5] = expiredAtUnixSeconds (빈 문자열 허용)
-- ARGV[6] = entryKeyPrefix ("IdKeeper/XApiKey/{XApiKey}/")
--
-- 에러: DUPLICATE_APIKEY(이미 존재)
-- 반환: 새로 채번된 id

local byApiKeyIndex, seqKey, allKey = KEYS[1], KEYS[2], KEYS[3]
local apiKey = ARGV[1]
local owner = ARGV[2]
local description = ARGV[3]
local createdAt = ARGV[4]
local expiredAt = ARGV[5]
local entryPrefix = ARGV[6]

if redis.call('EXISTS', byApiKeyIndex) == 1 then
	return redis.error_reply('DUPLICATE_APIKEY')
end

local id = redis.call('INCR', seqKey)
local hkey = entryPrefix .. id

redis.call('HSET', hkey,
	'ApiKey', apiKey,
	'Owner', owner,
	'Description', description,
	'CreatedAtUtc', createdAt,
	'ExpiredAtUtc', expiredAt)
redis.call('SET', byApiKeyIndex, id)
redis.call('SADD', allKey, id)

return id
