-- KEYS[1] = Identity Role Hash
-- KEYS[2] = Role ByNormalizedName 유니크 인덱스
-- ARGV[1] = normalizedName
-- ARGV[2] = roleId (인덱스에 저장할 값)
-- ARGV[3..] = HSET용 필드/값 쌍
--
-- 에러: DUPLICATE

local roleKey, byNameKey = KEYS[1], KEYS[2]
local normalizedName = ARGV[1]
local roleId = ARGV[2]

if redis.call('EXISTS', byNameKey) == 1 then
	return redis.error_reply('DUPLICATE')
end

for i = 3, #ARGV, 2 do
	redis.call('HSET', roleKey, ARGV[i], ARGV[i + 1])
end
redis.call('SET', byNameKey, roleId)

return 'OK'
