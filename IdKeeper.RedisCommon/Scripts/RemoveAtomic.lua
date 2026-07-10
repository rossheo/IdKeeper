-- KEYS[1] = AllocatedId Bitmap
-- KEYS[2] = AllocatedId ByRequester Set (해당 requester)
-- KEYS[3] = AllocatedId ExpiryIndex ZSET
-- ARGV[1] = entryKeyPrefix ("IdKeeper/AllocatedId/{AllocatedId}/")
--
-- IgnoreExpire=true(영구 ID)는 삭제하지 않고 보존한다.
--
-- AuditLog는 {AuditLog} 해시태그가 달라 이 스크립트와 같은 슬롯에 있다는 보장이 없어
-- (Redis Cluster에서 EVAL은 KEYS가 전부 같은 슬롯이어야 함) 감사 로그 기록은
-- 호출부(AllocatedIdRepository.RemoveAsync)에서 별도로 수행한다.

local bitmapKey, byReqKey, expiryKey = KEYS[1], KEYS[2], KEYS[3]
local entryPrefix = ARGV[1]

local candidateIds = redis.call('SMEMBERS', byReqKey)
local removed = {}

for _, id in ipairs(candidateIds) do
	local hkey = entryPrefix .. id
	local ignoreExpire = redis.call('HGET', hkey, 'IgnoreExpire')
	if ignoreExpire == '0' then
		redis.call('SETBIT', bitmapKey, tonumber(id), 0)
		redis.call('DEL', hkey)
		redis.call('SREM', byReqKey, id)
		redis.call('ZREM', expiryKey, id)
		table.insert(removed, id)
	end
end

return removed
