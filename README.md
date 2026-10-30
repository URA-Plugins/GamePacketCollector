# GamePacketCollector

GamePacketCollector 是 URA 的游戏包采集插件,捕获高价值游戏 request-response raw 包并上传到 URACloud GamePackets。

插件通过唯一的程序化 analyzer API，以 `Register<ReadOnlyMemory<byte>>` 分别注册 request/response；endpoint 集合由 exact 与不跨 `/` 的 wildcard pattern 在 Host catalog 中展开。插件采集原始 MessagePack bytes，不解析 response payload。插件按 endpoint FIFO 配对 request/response，生成完整 HTTP exchange 后上传。当前采集类别:

- `single-mode`: 育成开始、恢复、事件、训练/行动、比赛、技能、因子、剧本专属动作、结算。
- `room-match`: 自定义比赛房间、报名、轮询、开赛、结果、历史结果、比赛条件。
- `race`: Champions、TeamStadium、PracticeRace、ChallengeMatch、DailyRace、UltimateRace 等比赛核心链路。
- `gacha`: `Gacha.Exec`、抽卡历史与奖品历史。

采集包先写入插件数据目录下的 `pending/*.json`,上传成功后删除 pending 文件。插件持有上传任务，并在 `DisposeAsync` 中取消和等待。上传循环按文件大小 pacing; HTTP 408/429/5xx 和网络异常保留 pending 并重试,其它 HTTP 失败移动到 `failed/yyyyMMdd/`。每个 pending 文件单独上传到 URACloud `/GamePackets`。配置文件为 `PluginData/游戏包采集/config.json`; 文件不存在时，插件在 `StartAsync` 中使用 `context.Application` 创建 Terminal.Gui dialog。选项区使用 Terminal.Gui 原生 `Menu` 和 `CheckBox` command views,支持 Up/Down、hover、Enter 和 click。取消不会写入配置或注册 analyzer,首次配置保存后立即按所选配置激活:

```json
{"uploadUrl":"https://ura.shuise.net/api/GamePackets","serverRegionHint":null,"enabled":true,"endpointGroups":["single-mode","gacha"]}
```

pending 文件是 URACloud `/GamePackets` 的上传体:

```json
{"schemaVersion":"2","kind":"game-packet","packetIdemKey":"q7GM4Ai8B0V9TgdVMRCGsQ1jEmrh3K4ptYqz6RU8RFc","endpointType":"Gallop.Endpoints.GameApi.Gacha.Exec","endpointPath":"/umamusume/gacha/exec","group":"gacha","request":"...","response":"...","serverRegionHint":null,"sid":"request-sid-1","gameDataVersion":"2026070301","appVersion":"1.2.3","viewerId":"123456789"}
```

`sid` 来自真实 `X-Hachimi-sid` request header，缺失时该包 fail fast。response 原包保留 `data_headers.sid` 与 `data_headers.servertime`；URACloud 从 raw response 派生 `next_request_sid` 与 `response_server_time`，采集插件不解析这些字段。

`appVersion`、`gameDataVersion`、`viewerId` 来自宿主传入的真实 `X-Hachimi-*` request headers，用于生成 `packetIdemKey`；缺失时该包 fail fast。

## 构建

```powershell
git -c core.longpaths=true submodule update --init --recursive
dotnet build .\GamePacketCollector.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"
```

## 验证与发布

`tests/GamePacketCollector.Tests` 覆盖端点集合、原包配对与幂等键、配置、并发落盘、上传失败分类、失败通知和关闭时保留 pending，并加载本轮 ZIP 验证 loopback PUT、原始字节与头字段、程序集来源及清理。

测试使用 `UraTestHostRoot` 指定匹配的 Host 源码；生产插件继续引用 NuGet。默认入口先执行本仓发布边界检查。运行前将 `URA_PLUGIN_SMOKE_PACKAGE_ROOT` 设为本轮构建 ZIP 所在目录，按 manifest 的 `InternalName` 唯一匹配；缺包或重复包明确失败。GitHub Actions 未设置该变量时使用 `GITHUB_WORKSPACE/artifacts`。`--package-load` 单独运行 ZIP 验证。本仓测试工具位于 `tests/Support`，上传测试只连接本地临时服务。

`tests/GamePacketCollectorReplaySmoke` 默认运行捕获解析、三语言错误识别与合成包上传自测；`--self-test` 仅执行解析与错误识别，真实回放需传 `--corpus <目录>`。目录顶层 `*.txt` 必须以 `[整数序号]` 开头，包含 notify HTTP 头和原始 body。回放启用采集并只上传到测试创建的 `127.0.0.1` 临时 HTTP 对端，校验原始字节、头字段和配对，报告已验证上传与 pending 数量；空语料、长度或 URL/SID 配对错误、未知端点、分发异常及可见 Error 均失败。Error 检查仅针对当前 framebuffer 的可见内容，不检查屏幕外历史或其他 workspace 的日志。真实捕获和临时回放输出不入库。

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。
