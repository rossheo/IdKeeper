-- KEYS[1] = XApiKey 항목 Hash
-- KEYS[2] = XApiKey ByApiKey 유니크 인덱스
-- KEYS[3] = XApiKey All Set
-- ARGV[1] = id (All Set에서 제거할 값)

local hkey, byApiKeyIndex, allKey = KEYS[1], KEYS[2], KEYS[3]
local id = ARGV[1]

redis.call('DEL', hkey)
redis.call('DEL', byApiKeyIndex)
redis.call('SREM', allKey, id)

return 'OK'
