# Dignite Paperbase

这是一个 ABP 框架的项目，遵循 `.claude/rules/` 中的 ABP 核心约定和模块模板。

## 项目组织

项目分为四个主要目录：
- **core/** - ABP 应用程序核心，遵循 abp-core.md 规则
- **modules/** - 可复用业务模块，每个模块遵循 module-template.md 的结构和虚拟方法要求
- **host/** - 单租户测试主机，仅在此配置中间件（OnApplicationInitialization）
- **docs/** - 项目文档

## 处理规则

1. 在 core 和 modules 中开发时，严格遵循 `.claude/rules/` 中的规则
2. 开发可复用模块时，**所有公共和受保护方法必须是虚拟（virtual）的**
3. 模块中不要配置中间件，仅在 host 中配置
4. 遵循 ABP 的依赖注入约定，不要手动调用 AddScoped/AddTransient/AddSingleton