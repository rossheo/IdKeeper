-- KEYS[1] = AllocatedId Bitmap
-- KEYS[2] = AllocatedId ByRequester Set (해당 requester)
-- KEYS[3] = AllocatedId ExpiryIndex ZSET
-- ARGV[1] = maxBitInclusive
-- ARGV[2] = count
-- ARGV[3] = startBit (핫스팟 회피용 랜덤 오프셋)
-- ARGV[4] = requester
-- ARGV[5] = createdAtUnixSeconds
-- ARGV[6] = expiredAtUnixSeconds
-- ARGV[7] = entryKeyPrefix ("IdKeeper/AllocatedId/{AllocatedId}/")
-- ARGV[8] = description (빈 문자열 허용)
--
-- 에러: ALREADY_EXISTS(동일 requester 이미 존재), INSUFFICIENT_IDS(여유 ID 부족)
--
-- AuditLog는 {AuditLog} 해시태그가 달라 이 스크립트와 같은 슬롯에 있다는 보장이 없다
-- (Redis Cluster에서 EVAL은 KEYS가 전부 같은 슬롯이어야 함) — 그래서 감사 로그 기록은
-- 호출부(AllocatedIdRepository.AllocAsync)에서 이 스크립트 성공 후 별도로 수행한다.

local bitmapKey, byReqKey, expiryKey = KEYS[1], KEYS[2], KEYS[3]

local maxBit = tonumber(ARGV[1])
local count = tonumber(ARGV[2])
local total = maxBit + 1
local startBit = tonumber(ARGV[3]) % total
local requester = ARGV[4]
local createdAt = ARGV[5]
local expiredAt = ARGV[6]
local entryPrefix = ARGV[7]
local description = ARGV[8]

if redis.call('SCARD', byReqKey) > 0 then
	return redis.error_reply('ALREADY_EXISTS')
end

local found = {}
for i = 0, maxBit do
	if #found >= count then
		break
	end
	local bit = (startBit + i) % total
	if redis.call('GETBIT', bitmapKey, bit) == 0 then
		table.insert(found, bit)
	end
end

if #found < count then
	return redis.error_reply('INSUFFICIENT_IDS')
end

for _, id in ipairs(found) do
	redis.call('SETBIT', bitmapKey, id, 1)
	local hkey = entryPrefix .. id
	redis.call('HSET', hkey,
		'Requester', requester,
		'CreatedAtUtc', createdAt,
		'UpdatedAtUtc', '',
		'ExpiredAtUtc', expiredAt,
		'IgnoreExpire', '0',
		'Description', description)
	redis.call('SADD', byReqKey, id)
	redis.call('ZADD', expiryKey, expiredAt, id)
end

return found
