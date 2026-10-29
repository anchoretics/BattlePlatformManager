# 独立 Windows 用户的战网浏览器认证验证

验证日期：2026-10-29。环境：Windows 11 Pro 25H2（26200.6899），Battle.net 2.53.6.17952；先测试 wow2，再用全新账号 wow3 独立复测。

## 已验证的结果

无需进入目标用户的 Windows 桌面，可以在主账号桌面上完成战网页面认证，并返回战网主界面。wow2 和全新账号 wow3 的测试中，用户均确认游戏正常进入。最初通过临时诊断助手验证成功；现已接入 BPM v1.1.0，增加状态界面、按配置开关、内存管道与人工兜底。集成后的 Debug 版本也已获用户确认：自动打开网页、认证成功、游戏正常进入。

可行路径为：创建并初始化用户配置文件，由 BPM 启动目标用户的战网；实时取得战网真正交给 Windows 的完整认证 URL，原样交给同一用户的 Edge。用户确认网页与战网均登录成功，客户端日志出现 `ReceivedToken`。

wow2 的成功环境曾保留已初始化的 HTTP/HTTPS UserChoice，因此最初尚不能证明默认浏览器初始化是否必要。后续 wow3 的首次启动和认证完成后均确认没有 HTTP/HTTPS UserChoice 或 UserChoiceLatest，仍完成认证和游戏进入。这一对照在本机证明转交路径无需先设置默认浏览器或生成 Hash。战网原有浏览器交接为什么没有自行完成，仍未定位到最终失败点。

## 对照实验

| 方法 | 实际结果 |
| --- | --- |
| 在 wow2 下启动 Edge，再从设置页设默认 | 设置实际落到桌面用户 anchor，不能据浏览器进程属主推断系统设置所属用户；anchor 已恢复 Chrome |
| 仅注册 wow2 的 Classes HTTP/HTTPS 打开命令 | 合并注册表视图已采用命令，但实际关联仍是 OpenWith / Undecided；网页打开错误 1155，新增项已撤回 |
| 为 wow2 生成 UserChoice Hash | Windows 接受两个协议的 MSEdgeHTM 选择；32/64 位查询和普通网页可见性均通过，但设置后重新启动的战网仍未自行打开认证页 |
| 从战网日志取网易 OAuth URL | 可打开页面；新链接约 0.196 秒内打开仍超时，未完成认证 |
| 从日志取带 ref 的战网前置页 | 网页仍提示“使用浏览器完成登录”，未完成认证 |
| 实时捕获真正的外部认证 URL，再启动目标用户 Edge | 验证成功，用户确认进入主界面，日志出现 ReceivedToken |

## 实际认证入口的差异

实时 Shell-Core 事件中的完整入口是 `account.battlenet.com.cn` HTTPS 地址，包含 `flow`、`ref`、`return`、`returnHttps`、`secure`、`state` 参数。本次地址长度 643，与战网日志中的任一原文不完全相同。

此前从日志取得的网易地址属于 `oauth.g.mkey.163.com`，长度 359。另一个日志前置页面使用 `ref`、`providerId`、`captchaRequired`、`isMASDK` 参数。实验表明不能用这两个地址直接替代实际外部入口；尚未证明两种失败的全部服务端原因。

诊断中的 `CDesktopFolder_ParseDisplayName` 返回过 `0x80070057`，但正常打开 Example Domain 的对照也出现相同中间返回值，因此不能将其认定为最终启动失败原因。

## 验证助手的处理方式

使用微软 TraceEvent 创建临时实时 ETW 会话，先按程序名过滤，再核对来源进程属于 wow2。只对战网当前新事件中的指定 HTTPS 认证站点、预期参数结构和时效进行验证；完整 URL 仅在内存中原样传给目标用户 Edge，不通过命令解释器执行，不写入诊断报告。

同一请求只转交一次。两次实际转交均返回 Win32Error=0，从事件时间到启动完成分别约 1.44 秒、1.87 秒。用户随后确认认证成功。完成后已正常停止临时会话，未保留后台监听。

## 从客户端退出状态进行的复测

用户退出战网后，已确认战网、Agent 和游戏进程均未运行，再建立新的限时会话，调用用户当前 Debug 目录的 `BPM.exe --launch` 启动保存的 wow2 配置。BPM 正常退出（ExitCode=0），本轮战网进程属主仍为 wow2。

用户观察为直接自动登录、未出现新的输入登录页面；事件与日志则确认 BPM 启动后的客户端确有一次新认证请求，完整 URL 约 1.63 秒内交给 Edge，客户端再次出现 `ReceivedToken`。停止监测前另一个新战网进程又发起了一次请求，约 0.79 秒内转交；这条记录单独保留，不冒充第一次 BPM 启动的请求。可能复用了浏览器已有的登录状态，但未独立证明这一机制，因此不将本轮等同于重新输入密码、短信认证的测试。

本次配置 `AutoLaunchWow=false`，游戏未由 BPM 自动拉起。已请求用户在本轮战网中启动游戏，结果待反馈；在此之前不能声称从启动到游戏进入的全部流程均已通过。认证监测已停止，战网客户端保留供游戏验证。

后续检查发现第二个客户端已经退出，其日志没有 ReceivedToken；首次由 BPM 启动的成功认证证据仍单独保留。退出日志显示有序关闭多个服务，不能仅据此判断退出由谁发起。当前没有战网或游戏进程，已询问用户是否手动退出；完整流程的游戏阶段仍未通过验收。

用户随后确认是手动退出，并反馈再次登录仍不弹浏览器。上一轮助手在游戏验证完成前已停止，产品尚未集成转交功能；因此后续单独启动客户端缺少这一支持，不能将其当作已验证方法失效或客户端闪退。已准备继续保持助手，限定最长 30 分钟，保留当前客户端并跟随 wow2 后续重启，待认证和游戏阶段都验收后统一清理。该轮资料位于 `artifacts/diagnostics/shell-auth-game-verification-20261029`。

### 完整流程复测的最终结果

用户最终确认“认证成功，游戏也正常进入”。本轮证据对应关系如下：

1. BPM 自身日志记录在北京时间 17:21:37 为 wow2 启动战网 PID 12716；与当前客户端的进程创建时间、属主一致，确认客户端来自 BPM 启动。
2. 恢复并保持临时助手后，捕获该客户端的新认证请求，在约 2.04 秒内交给目标用户 Edge，原生启动返回 Win32Error=0。
3. 同一客户端日志出现 `ReceivedToken`，用户确认战网认证完成。
4. 游戏正常进入由用户明确确认。收尾进程快照未捕获到常见游戏进程，因此不额外宣称已独立核对游戏 PID、属主或长时间运行稳定性。
5. 用户确认两个阶段均完成后，才停止本轮助手；最终 `State=Stopped`，监测进程已退出。未关闭战网或游戏，anchor 的 HTTP/HTTPS 仍为 ChromeHTML。

结果：本机使用临时助手的 BPM 启动、真实网页认证转交、返回战网和用户游戏进入验证通过。这是一次功能验证，未覆盖重启 Windows 后首次运行，也不等于 BPM 已永久修复；后续独立启动仍需转交助手，直到该机制正式接入产品。

补充证据：`game-verification.json`、`completion-check.json`、`relay.sanitized.jsonl`，均在上述本轮资料目录中，未保存完整认证链接或参数值。

### 全新账号复测准备

用户计划重新创建一个账号再次测试。助手已增加显式配置 ID 选择，按该配置对应的本地用户 SID 过滤事件、验证凭据身份，拒绝选择操作者自身账号；不再固定使用 wow2。只读选取 wow1 的检查通过，未启动目标用户进程、读取凭据或更改浏览器。

下一轮保留新用户浏览器关联原状，不预先写 UserChoice，也不为采集基线提前以新用户运行探针，避免影响第一次 BPM 启动。助手会只读记录用户配置加载前后关联状态，并保持到认证和游戏阶段验收结束（最长 30 分钟）。新配置尚未保存，暂未启动该轮测试；通用助手位于 `artifacts/diagnostics/fresh-user-harness`。

用户已保存新配置并确认 Windows 用户名为 wow3。只读核对显示本地账号已存在、启用，配置目录为 C:\Users\wow3；按配置 ID 选择结果正确，未读取凭据或提前运行 wow3 进程。该轮助手已准备于 `artifacts/diagnostics/fresh-user-wow3-20261029`，将由 BPM 首先启动目标用户进程，不修改其默认浏览器。当前仍等待 Windows UAC 批准，尚无启动、认证或游戏结果。

### wow3 全新账号测试结果

用户最终确认“认证成功，游戏正常进入”，核对结果如下：

- 启动前 wow3 用户注册表配置尚未加载；助手没有提前运行该用户的探针。通过当前 Debug BPM 的 `--launch` 入口启动新配置，ExitCode=0。
- BPM 启动后立即读取到已加载的 wow3 用户配置：HTTP、HTTPS 均无 UserChoice / UserChoiceLatest。此时尚未开始网页认证。
- 战网新进程 PID 15560 发出实际完整认证地址，约 0.93 秒内交给 wow3 的 Edge，Win32Error=0。
- 以 wow3 自身权限只读核对，战网日志出现 `ReceivedToken`；认证完成后两个协议仍无 UserChoice / UserChoiceLatest。本轮没有生成保护 Hash，也没有写入默认浏览器关联。
- 游戏正常进入由用户确认；不额外宣称已独立核对游戏进程属主或长时间运行稳定性。
- 本轮临时事件会话已正常停止，进程已退出；只读检查的临时 PowerShell 进程也没有遗留，anchor 的两个协议仍为 ChromeHTML。

操作者直接读取 wow3 私有注册表时曾被权限拒绝，首次收尾 IPC 探针超时；最终用已保存凭据做短时只读身份模拟，读取 wow3 自己的配置和日志后立即恢复操作者身份，无需额外 Windows UAC。该检查发生在用户已经确认认证及游戏进入之后，不影响首次启动条件，也没有建立 Windows 桌面会话。

结论：在本机，新建账号无需人工登录其 Windows 桌面、无需设置默认浏览器，通过真实认证地址转交即可完成认证并进入游戏。正式接入时可以去掉“先改默认浏览器”的前置步骤；仍需处理管理员权限、账户隔离、事件格式兼容和助手生命周期。本轮未重启 Windows，功能尚未接入 BPM 产品。

本轮证据：`browser-baseline-before-launch.json`、`browser-baseline-after-launch.json`、`bpm-launch.json`、`relay.sanitized.jsonl`、`post-test-user-context.json`、`final-verification-summary.json`，均位于上述 wow3 目录，未保存完整认证链接或参数值。

复测证据位于 `artifacts/diagnostics/shell-auth-fulltest-20261029`，包括 `bpm-launch.json`、`relay.sanitized.jsonl`、`startup-observation.json`、`auth-verification.json`。

测试没有修改主账号默认浏览器、系统策略或驱动。wow2 保留早期实验的 Edge 协议关联和恢复状态；wow3 则始终没有这两项协议的用户选择。UCPD 在实验开始前已处于禁用、停止状态，本轮没有改变其状态做额外对照。

## 产品实现的边界

2026-10-29 产品接入增加了以下检查：自动化回归 153 项通过；主界面、最小尺寸与账号编辑窗口已做 WPF 渲染检查。实际以 wow3 运行新版 BPM 最小助手时，原 Debug 目录先返回 267，改用系统工作目录后返回 5，确认目标用户无权读取操作者的私有程序目录。增加仅含程序文件的受限缓存后，Debug 与自包含单文件包的正确 SID 助手均成功连接管道并拒绝无效测试地址；未打开测试网页，未修改默认应用。证据位于 `artifacts/verification/browser-assist/pipe-smoke.json` 和 `published-pipe-smoke.json`。管理员产品检查已通过：会话 Ready，退出后所属会话数为 0，wow3 助手 SID/管道及无效地址拒绝正确，证据见 elevated-smoke.json。用户随后用集成后的 Debug BPM 确认：wow3 自动打开网页、认证成功、游戏正常进入；该结果为新版产品验收，独立于先前临时研究助手。后续自动提权与图标更新回归 160 项通过，wow3 普通助手仍可直接运行，未触发额外提权。

可将“取得真实认证请求并交给目标用户浏览器”作为浏览器交接异常时的候选恢复功能。正式实现还需要：

- 明确管理员权限要求，限定诊断的启动时机、超时、停止和异常清理。
- 过滤到目标账号、可信战网进程、必要事件及认证站点，处理进程退出和 PID 重用。
- 将取消、重试和新请求分别处理，去重，拒绝过期请求，不拼接或猜测认证参数。
- 采用原生 Windows 参数传递，避免命令解释器；不记录完整 URL、参数值、凭据或浏览器命令行。
- 为事件格式变化、缺失事件和浏览器启动失败提供可诊断结果；不能把 ETW 事件布局当作战网公开稳定接口。
- 将实际关联查询、网页显示、认证返回分开验收；API 成功不等于用户已登录。

已验证的转交路径不必写 UserChoice，不应把禁用 UCPD 或覆盖默认应用选择作为修复步骤。当前只是本机验证，尚未形成跨版本支持承诺。

## 本机证据

以下均为 `.gitignore` 排除的本机 artifacts，报告只含脱敏信息：

- `artifacts/diagnostics/wow2-userchoice-test/userchoice-apply.result.json`
- `artifacts/diagnostics/wow2-userchoice-test/userchoice-x86-browser.result.json`
- `artifacts/diagnostics/shell-auth-trace/events.sanitized.jsonl`
- `artifacts/diagnostics/shell-auth-relay/relay.sanitized.jsonl`
- `artifacts/diagnostics/shell-auth-relay/verification-summary.json`

研究助手源码保留于 `artifacts/research/default-browser`，不进入发布包。产品实现位于 `BrowserLoginAssistService`、`BrowserLoginPipe` 和 `BrowserAuthenticationPolicy`，与研究助手分离；具体接入与检查边界见 Architecture/TestPlan。

参考：[微软 TraceEvent](https://github.com/microsoft/perfview/blob/main/documentation/TraceEvent/TraceEventLibrary.md)、[实时 ETW 权限](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-opentracew)、[默认应用平台限制](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform)、[PS-SFTA 原始实现](https://github.com/DanysysTeam/PS-SFTA/tree/22a32292e576afc976a1167d92b50741ef523066)。
