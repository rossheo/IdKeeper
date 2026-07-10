-- XApiAllowedCidr / XApiAllowedHostname / FeatureSwitch 공용: 자연키(natural key)를
-- Redis 키 자체로 사용하는 엔티티의 원자적 생성.
-- KEYS[1] = 엔티티 항목 Hash (자연키 포함)
-- KEYS[2] = All Set
-- ARGV[1] = naturalKeyValue (All Set에 추가할 값)
-- ARGV[2..] = HSET용 필드/값 쌍
--
-- 에러: DUPLICATE(이미 존재)

local entryKey, allKey = KEYS[1], KEYS[2]
local naturalKey = ARGV[1]

if redis.call('EXISTS', entryKey) == 1 then
	return redis.error_reply('DUPLICATE')
end

for i = 2, #ARGV, 2 do
	redis.call('HSET', entryKey, ARGV[i], ARGV[i + 1])
end
redis.call('SADD', allKey, naturalKey)

return 'OK'
