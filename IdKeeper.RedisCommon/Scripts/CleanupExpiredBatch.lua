-- KEYS[1] = AllocatedId Bitmap
-- KEYS[2] = AllocatedId ExpiryIndex ZSET
-- ARGV[1] = entryKeyPrefix ("IdKeeper/AllocatedId/{AllocatedId}/")
-- ARGV[2] = byRequesterKeyPrefix ("IdKeeper/AllocatedId/{AllocatedId}/ByRequester/")
-- ARGV[3..] = 만료 처리 대상 id 목록(C#에서 ZRANGEBYSCORE로 청크 조회한 결과)
--
-- 원본 CleanupExpiredJob과 동일하게 감사로그를 남기지 않는다.

local bitmapKey, expiryKey = KEYS[1], KEYS[2]
local entryPrefix = ARGV[1]
local byReqPrefix = ARGV[2]
local removed = {}

for i = 3, #ARGV do
	local id = ARGV[i]
	local hkey = entryPrefix .. id
	local requester = redis.call('HGET', hkey, 'Requester')
	redis.call('SETBIT', bitmapKey, tonumber(id), 0)
	redis.call('DEL', hkey)
	redis.call('ZREM', expiryKey, id)
	if requester then
		redis.call('SREM', byReqPrefix .. requester, id)
	end
	table.insert(removed, id)
end

return removed
