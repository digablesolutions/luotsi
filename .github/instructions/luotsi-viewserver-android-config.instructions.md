---
name: "Luotsi Android Config"
description: "Use when editing Luotsi.ViewServer.Android AndroidManifest.xml or build.gradle.kts files. Covers thin-helper boundaries, permissions, exported components, foreground services, SDK levels, minimal dependency/plugin surface, and Gradle Kotlin DSL conventions."
applyTo:
  - "Luotsi.ViewServer.Android/**/AndroidManifest.xml"
  - "Luotsi.ViewServer.Android/**/*.gradle.kts"
---

# Luotsi Android Config

- Treat the Android module as a thin capture/transport helper. Do not move host orchestration, reconnect policy, or operator UX into manifest or build configuration churn.
- Keep the permission surface minimal. Every new permission needs a concrete runtime requirement.
- Keep Android component exposure minimal. Only export activities, services, or receivers when Android or the product flow requires it.
- Foreground service declarations must match the actual workload and target SDK behavior, especially for MediaProjection capture.
- Keep Gradle Kotlin DSL small and explicit. Avoid adding plugins, libraries, codegen, or architecture frameworks unless the helper's scope clearly justifies them.
- Prefer AndroidX-first, Kotlin-first configuration. Keep SDK levels and Java/Kotlin toolchain choices intentional and aligned with the helper's actual needs.
- Be cautious with manifest changes that affect notifications, service types, process behavior, or package/component names because the host CLI depends on several of those identifiers.
- If a config change can alter runtime capture or transport behavior, add or update targeted tests and host-side coordination as needed.
