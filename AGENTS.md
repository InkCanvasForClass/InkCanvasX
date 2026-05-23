# Project Memory - LanStartWrite.Inkcanvas

## Critical: This is a Jalium.UI project

**THIS IS NOT WPF. THIS IS NOT WinUI. THIS IS NOT Avalonia.**

This project uses **Jalium.UI** framework. All UI code, markup, and patterns must follow Jalium.UI conventions.

---

## Jalium.UI Framework Reference

### What is Jalium.UI?
Jalium.UI is a GPU-accelerated UI framework for .NET 10. It combines:
- **WPF-style object model**: DependencyObject, DependencyProperties, visual tree, logical tree, routed events, data binding
- **JALXAML markup language**: Declarative UI markup with namespace `https://schemas.jalium.dev/jalxaml/presentation`, file extension `.jalxaml`, supports Source Generator compile-time code generation
- **Razor syntax extensions in JALXAML**: `@Path`, `@(expr)`, `@{ ... }`, `@if`, mixed text templates
- **GPU-native rendering backends**: DirectX 12 (Windows), Vulkan (Linux/Android), Metal (macOS), Software fallback

### Current Version
v26.10.2 (latest release as of 2026-05-06)

### Platform Targets
- **Primary**: Windows 10/11 x64 (DirectX 12)
- **Cross-platform**: Android (arm64-v8a, x86_64), Linux (Vulkan), macOS (Metal)
- **Runtime**: .NET 10 (net10.0-windows, net10.0-android, net10.0)

### Key Differences from WPF
- **Rendering**: DirectX 12 (not DirectX 9 like WPF). Uses Vello GPU compute pipeline.
- **Markup**: JALXAML (`.jalxaml` files), NOT XAML. Namespace: `https://schemas.jalium.dev/jalxaml/presentation` or `https://jalium.dev/ui`
- **Razor syntax**: JALXAML supports Razor-style syntax (`@Path`, `@(expr)`, `@{ ... }`, `@if`) as additive sugar on top of `{Binding ...}`
- **Startup**: Must call `ThemeLoader.Initialize()` BEFORE any JALXAML parsing
- **NOT a drop-in WPF replacement**: API names are close to WPF but differences exist intentionally

### Package Structure
| Package | Responsibility |
|---------|---------------|
| Jalium.UI.Core | Dependency property system, visual tree, layout, routed events, binding, animation |
| Jalium.UI.Media | Brushes, geometry, drawing, text formatting, imaging, visual effects |
| Jalium.UI.Input | Mouse, keyboard, touch, stylus input abstractions |
| Jalium.UI.Interop | Managed/native bridge, P/Invoke, runtime native dependency packaging |
| Jalium.UI.Gpu | GPU resource management, render graph, materials, shaders, backend abstraction |
| Jalium.UI.Controls | Controls, panels, templates, windowing, themes, docking, charts |
| Jalium.UI.Xaml | JALXAML parse/load pipeline, Razor syntax support, markup services |
| Jalium.UI.Build | MSBuild tasks for JALXAML compilation workflow |
| Jalium.UI.Xaml.SourceGenerator | Roslyn source generator for XAML/code-behind integration |
| Jalium.UI | Metapackage that references the full framework stack |

### Platform Packages
- **Jalium.UI.Desktop**: net10.0-windows distribution with native DLLs
- **Jalium.UI.Android**: net10.0-android distribution with native .so libraries

### Startup Pattern (Critical Order!)
```
ThemeLoader.Initialize() -> new Application() -> XamlReader.Parse(...) -> new Window { Content = ... } -> app.Run(window)
```
- Only ONE Application instance per process
- `[STAThread]` must be applied to entry method
- ThemeLoader.Initialize() must run before ANY JALXAML parsing

### Available Controls (80+)
- **Input**: Button, TextBox, PasswordBox, NumberBox, AutoCompleteBox, ComboBox, Slider, CheckBox, RadioButton
- **Data**: TreeView, DataGrid, TreeDataGrid, ListBox, ListView
- **Navigation**: NavigationView, TabControl, Ribbon, CommandBar, MenuBar
- **Documents**: FlowDocumentViewer, FlowDocumentReader, FlowDocumentScrollViewer, Markdown
- **Charts**: Category, DateTime, Logarithmic axes with chart legend
- **Rich**: InkCanvas, WebView/WebBrowser, EditControl, QRCode, TitleBar
- **Layout**: Grid, StackPanel, Canvas, DockPanel, WrapPanel, UniformGrid, VirtualizingStackPanel

### Visual Effects
- Liquid glass with refraction, chromatic aberration
- Backdrop effects: blur, acrylic, mica, frosted glass
- Transition shaders and element effects (blur, drop shadow)
- Custom shader support via HLSL

### Text Rendering
- ClearType sub-pixel text rendering with dual-source blending
- CPU rasterization fallback path
- Cross-platform text shaping via FreeType + HarfBuzz (Linux/Android)

### Resources
- GitHub: https://github.com/VeryJokerJal/Jalium.UI
- Official Site: http://jaliumui.top
- Docs: http://docs.jaliumui.top
- QQ Group: 1079778999
- License: MIT
