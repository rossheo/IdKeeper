-- KEYS[1] = AllocatedId ByRequester Set (해당 requester)
-- KEYS[2] = AllocatedId ExpiryIndex ZSET
-- ARGV[1] = entryKeyPrefix ("IdKeeper/AllocatedId/{AllocatedId}/")
-- ARGV[2] = updatedAtUnixSeconds
-- ARGV[3] = expiredAtUnixSeconds
--
-- AuditLog는 {AuditLog} 해시태그가 달라 이 스크립트와 같은 슬롯에 있다는 보장이 없어
-- (Redis Cluster에서 EVAL은 KEYS가 전부 같은 슬롯이어야 함) 감사 로그 기록은
-- 호출부(AllocatedIdRepository.RenewAsync)에서 별도로 수행한다.

local byReqKey, expiryKey = KEYS[1], KEYS[2]
local entryPrefix = ARGV[1]
local updatedAt = ARGV[2]
local expiredAt = ARGV[3]

local ids = redis.call('SMEMBERS', byReqKey)
if #ids == 0 then
	return {}
end

-- IgnoreExpire=true인 id도 UpdatedAtUtc/ExpiredAtUtc 갱신 대상에 포함(원본 동작 유지).
-- ExpiryIndex는 IgnoreExpire=false인 것만 갱신한다.
for _, id in ipairs(ids) do
	local hkey = entryPrefix .. id
	redis.call('HSET', hkey, 'UpdatedAtUtc', updatedAt, 'ExpiredAtUtc', expiredAt)
	local ignoreExpire = redis.call('HGET', hkey, 'IgnoreExpire')
	if ignoreExpire == '0' then
		redis.call('ZADD', expiryKey, expiredAt, id)
	end
end

return ids
