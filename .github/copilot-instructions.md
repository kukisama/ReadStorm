# Copilot 项目指令（ReadStorm）

> 这里用于放你后续的提示词规范。Copilot 在当前仓库里会优先参考该文件。

## 项目背景
- 项目：`ReadStorm`
- 技术栈：`.NET + Avalonia`
- 目标：阅读器桌面应用，强调分层清晰（Application / Domain / Infrastructure / Desktop）

## 代码生成约束（可按需改）
1. 优先保持现有分层架构，不跨层直接引用。
2. 新增代码默认使用 C# 现代语法，但避免过度炫技。
3. 公共 API 需有清晰命名与最小必要注释。
4. 优先小步修改，避免无关重构。
5. 修改后优先保证能通过构建与测试。

## 目录与职责（可持续补充）
- `src/ReadStorm.Domain`：核心模型与业务规则
- `src/ReadStorm.Application`：用例与应用服务抽象
- `src/ReadStorm.Infrastructure`：外部依赖实现
- `src/ReadStorm.Desktop`：Avalonia UI 与启动
- `tests/ReadStorm.Tests`：单元测试

## 你可以在这里追加的提示词示例
- “生成 ViewModel 时，优先考虑可测试性与依赖注入。”
- “UI 文案默认中文，异常信息对用户友好。”
- “新增服务先定义接口再落地实现。”

## 自定义工作流模板（可选）
当我提出需求时，请按以下顺序执行：
1. 先说明你理解的问题与边界。
2. 给出最小可行方案（MVP）与影响范围。
3. 实施代码修改。
4. 运行构建/测试并反馈结果。
5. 简要说明后续可优化点。
