-- KEYS[1] = Identity User Hash
-- KEYS[2] = User ByNormalizedUserName 유니크 인덱스
-- KEYS[3] = User ByNormalizedEmail 유니크 인덱스
-- KEYS[4] = User All Set
-- ARGV[1] = userId
-- ARGV[2] = normalizedUserName (빈 문자열 허용)
-- ARGV[3] = normalizedEmail (빈 문자열 허용)
-- ARGV[4..] = HSET용 필드/값 쌍
--
-- 에러: DUPLICATE_USERNAME / DUPLICATE_EMAIL

local userKey, byUserNameKey, byEmailKey, allKey = KEYS[1], KEYS[2], KEYS[3], KEYS[4]
local userId = ARGV[1]
local normalizedUserName = ARGV[2]
local normalizedEmail = ARGV[3]

if normalizedUserName ~= '' and redis.call('EXISTS', byUserNameKey) == 1 then
	return redis.error_reply('DUPLICATE_USERNAME')
end
if normalizedEmail ~= '' and redis.call('EXISTS', byEmailKey) == 1 then
	return redis.error_reply('DUPLICATE_EMAIL')
end

for i = 4, #ARGV, 2 do
	redis.call('HSET', userKey, ARGV[i], ARGV[i + 1])
end
if normalizedUserName ~= '' then
	redis.call('SET', byUserNameKey, userId)
end
if normalizedEmail ~= '' then
	redis.call('SET', byEmailKey, userId)
end
redis.call('SADD', allKey, userId)

return 'OK'
