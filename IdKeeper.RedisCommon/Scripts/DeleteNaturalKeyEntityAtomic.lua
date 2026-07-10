-- XApiAllowedCidr / XApiAllowedHostname / FeatureSwitch 공용: 자연키 엔티티의 원자적 삭제.
-- KEYS[1] = 엔티티 항목 Hash
-- KEYS[2] = All Set
-- ARGV[1] = naturalKeyValue (All Set에서 제거할 값)

local entryKey, allKey = KEYS[1], KEYS[2]
local naturalKey = ARGV[1]

redis.call('DEL', entryKey)
redis.call('SREM', allKey, naturalKey)

return 'OK'
