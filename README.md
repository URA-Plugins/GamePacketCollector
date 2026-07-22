# GamePacketCollector

GamePacketCollector 是 URA 的游戏包采集插件,捕获高价值游戏 request-response raw 包并上传到 URACloud GamePackets。

插件通过唯一的程序化 analyzer API，以 `Register<ReadOnlyMemory<byte>>` 分别注册 request/response；endpoint 集合由 exact 与不跨 `/` 的 wildcard pattern 在 Host catalog 中展开。插件采集原始 MessagePack bytes，不在本地解析、筛选字段或脱敏。插件按 endpoint FIFO 配对 request/response，生成完整 HTTP exchange 后上传。当前采集类别:

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
{"schemaVersion":"1","kind":"game-packet","capturedAt":"2026-07-03T12:30:00.0000000+08:00","packetIdemKey":"q7GM4Ai8B0V9TgdVMRCGsQ1jEmrh3K4ptYqz6RU8RFc","endpointType":"Gallop.Endpoints.GameApi.Gacha.Exec","endpointPath":"/umamusume/gacha/exec","group":"gacha","request":"...","response":"...","serverRegionHint":null,"sid":"sid-1","gameDataVersion":"2026070301","appVersion":"1.2.3","viewerId":"123456789","device":"android","deviceSubtype":"phone"}
```

`appVersion`、`gameDataVersion`、`viewerId` 来自宿主传入的真实 `X-Hachimi-*` request headers,用于生成 `packetIdemKey`; 缺失时该包 fail fast。`sid`、`device`、`deviceSubtype` 缺失时对应字段为 `null`。
