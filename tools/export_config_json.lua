-- Runs only configuration modules in an environment without IO, OS, FFI,
-- package loading, Python or debug access. Host IO is used by this wrapper only.
jit.off()
local source, list, destination, report = arg[1], arg[2], arg[3], arg[4]
local env = {setmetatable=setmetatable, pairs=pairs, ipairs=ipairs, type=type,
    tostring=tostring, tonumber=tonumber, table=table, math=math, string=string,
    assert=assert, error=error, select=select, unpack=unpack, next=next}
env._G = env
local function run(path)
    local fn, err = loadfile(path)
    if not fn then error(err) end
    setfenv(fn, env)
    debug.sethook(function() error('Configuration instruction budget exceeded') end, '', 10000000)
    local ok, result = pcall(fn)
    debug.sethook()
    if not ok then error(result) end
    return result
end
local function try_run(path)
    local f = io.open(path, 'rb')
    if f then
        f:close()
        run(path)
    end
end
try_run(source .. '/game_config_ConfigMeta.bytes')
env.me = {Pad_Adaptive=arg[5]=='tablet'}
try_run(source .. '/game_define_publicDefine_ViewDefine.bytes')
local function quote(s)
    return '"' .. s:gsub('[%z\1-\31\\"]', function(c)
        if c == '"' then return '\\"' end
        if c == '\\' then return '\\\\' end
        return string.format('\\u%04x', string.byte(c))
    end) .. '"'
end
local function json(v, visited)
    local t = type(v)
    if t == 'nil' then return 'null' end
    if t == 'boolean' then return tostring(v) end
    if t == 'number' then
        if v ~= v or v == math.huge or v == -math.huge then error('Non-finite number') end
        return string.format('%.17g', v)
    end
    if t == 'string' then return quote(v) end
    if t ~= 'table' then error('Non-data value: ' .. t) end
    visited = visited or {}
    if visited[v] then error('Cyclic table') end
    visited[v] = true
    local keys, count, max = {}, 0, 0
    for k in pairs(v) do
        keys[#keys+1] = k
        count = count + 1
        if type(k)=='number' and k > max then max = k end
    end
    local array = count > 0 and max == count
    for _,k in ipairs(keys) do
        if type(k) ~= 'number' or k < 1 or k % 1 ~= 0 then array = false end
    end
    local out = {}
    if array then
        for i=1,count do out[#out+1] = json(v[i],visited) end
    else
        table.sort(keys,function(a,b) return tostring(a)<tostring(b) end)
        local seen = {}
        for _,k in ipairs(keys) do
            local key=tostring(k)
            if seen[key] then error('JSON key collision') end
            seen[key]=true
            out[#out+1] = quote(key) .. ':' .. json(v[k],visited)
        end
    end
    visited[v] = nil
    return (array and '[' or '{') .. table.concat(out,',') .. (array and ']' or '}')
end
local results = {}
for name in io.lines(list) do
    local ok, result = pcall(function() return json(run(source .. '/' .. name .. '.bytes')) end)
    if ok then
        local output=assert(io.open(destination .. '/' .. name .. '.json','wb'))
        output:write(result,'\n');output:close()
        results[#results+1]={name=name,status='exported',bytes=#result}
    else
        results[#results+1]={name=name,status='failed',error=tostring(result)}
    end
end
local output=assert(io.open(report,'wb'));output:write(json(results),'\n');output:close()
print('Processed '..#results..' config modules')
