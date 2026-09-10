# GamePacketCollector

GamePacketCollector 是 URA 的游戏包采集插件,捕获高价值游戏 request-response raw 包并上传到 URACloud GamePackets。

插件通过唯一的程序化 analyzer API，以 `Register<ReadOnlyMemory<byte>>` 分别注册 request/response；endpoint 集合由 exact 与不跨 `/` 的 wildcard pattern 在 Host catalog 中展开。插件采集原始 MessagePack bytes，不解析 response payload。插件按 endpoint FIFO 配对 request/response，生成完整 HTTP exchange 后上传。当前采集类别:

- `single-mode`: 育成开始、恢复、事件、训练/行动、比赛、技能、因子、剧本专属动作、结算。
- `room-match`: 自定义比赛房间、报名、轮询、开赛、结果、历史结果、比赛条件。
- `race`: Champions、TeamStadium、PracticeRace、ChallengeMatch、DailyRace、UltimateRace 等比赛核心链路。
- `gacha`: `Gacha.Exec`、抽卡历史与奖品历史。

采集包先写入插件数据目录下的 `pending/*.json`,上传成功后删除 pending 文件。上传循环通过 `IPluginContext.RunBackground` 交给 Host 管理；卸载时由 Host 取消并等待。上传循环按文件大小 pacing; HTTP 408/429/5xx 和网络异常保留 pending 并重试,其它 HTTP 失败移动到 `failed/yyyyMMdd/`。每个 pending 文件单独上传到 URACloud `/GamePackets`。配置文件为 `PluginData/游戏包采集/config.json`; 文件不存在时，插件在 Host 的 `OnStarted` 回调中使用 `context.Application` 创建 Terminal.Gui dialog。选项区使用 Terminal.Gui 原生 `Menu` 和 `CheckBox` command views,支持 Up/Down、hover、Enter 和 click。取消不会写入配置或注册 analyzer,首次配置保存后立即按所选配置激活:

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

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。
