-- KEYS[1] = AllocatedId 항목 Hash (해당 id)
-- KEYS[2] = AllocatedId ExpiryIndex ZSET
-- ARGV[1] = id
-- ARGV[2] = newIgnoreExpire ("0"/"1")
--
-- 에러: NOT_FOUND(해당 id가 존재하지 않음)
--
-- AuditLog는 {AuditLog} 해시태그가 달라 이 스크립트와 같은 슬롯에 있다는 보장이 없어
-- (Redis Cluster에서 EVAL은 KEYS가 전부 같은 슬롯이어야 함) 감사 로그 기록은
-- 호출부(AllocatedIdRepository.ToggleIgnoreExpireAsync)에서 별도로 수행한다.

local hkey, expiryKey = KEYS[1], KEYS[2]
local id = ARGV[1]
local newIgnoreExpire = ARGV[2]

if redis.call('EXISTS', hkey) == 0 then
	return redis.error_reply('NOT_FOUND')
end

local expiredAt = redis.call('HGET', hkey, 'ExpiredAtUtc')
redis.call('HSET', hkey, 'IgnoreExpire', newIgnoreExpire)

if newIgnoreExpire == '1' then
	redis.call('ZREM', expiryKey, id)
else
	redis.call('ZADD', expiryKey, expiredAt, id)
end

return 'OK'
