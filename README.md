# Solar System - Vulkan/Veldrid Version

This project has been successfully upgraded from Microsoft XNA Framework to Veldrid, a modern cross-platform graphics library that supports Vulkan, Direct3D 11, OpenGL, and Metal.

## What Changed

### Framework Migration
- **From**: Microsoft XNA Framework 4.0 (.NET Framework 4.0)
- **To**: Veldrid 4.9.0 with Vulkan backend (.NET 8.0)

### Key Differences

#### Graphics API
- XNA used DirectX 9/11 under the hood
- Now using **Vulkan** through Veldrid for modern, high-performance graphics
- Cross-platform support (Windows, Linux, macOS)

#### Project Structure
- Migrated from old .csproj format to modern SDK-style project
- No longer requires XNA Game Studio or XNA redistributables
- Uses NuGet packages instead of GAC references

### New Dependencies
- `Veldrid` (4.9.0) - Core graphics abstraction
- `Veldrid.StartupUtilities` (4.9.0) - Window and device creation
- `Veldrid.SPIRV` (1.0.15) - Shader compilation from GLSL to SPIRV
- `Veldrid.ImageSharp` (4.9.0) - Image loading support

### Code Changes

#### Rendering Pipeline
- Replaced `SpriteBatch` with custom vertex/index buffers
- Implemented GLSL shaders (compiled to SPIRV for Vulkan)
- Manual texture management with `DeviceTexture` class
- Orthographic projection matrix for 2D rendering

#### Types & Namespaces
- `Microsoft.Xna.Framework.Vector2` → `System.Numerics.Vector2`
- `Microsoft.Xna.Framework.Color` → `Veldrid.RgbaFloat`
- `GraphicsDeviceManager` → `GraphicsDevice` (Veldrid)
- `Game` base class → Custom `IDisposable` implementation

#### Input Handling
- XNA's `Keyboard.GetState()` → Veldrid's `InputSnapshot`
- Event-based input processing

## Building and Running

### Prerequisites
- .NET 8.0 SDK
- Vulkan runtime (installed with graphics drivers on Windows)

### Build
```bash
dotnet build
```

### Run
```bash
dotnet run
```

## Controls
- **Space**: Restart current simulation
- **Right Arrow**: Next simulation mode
- **Left Arrow**: Previous simulation mode
- **Escape**: Exit

## Simulation Modes
1. **Simple Orbit**: 3 planets orbiting the sun
2. **Body Orbit Spiral**: 129 bodies in a spiral formation
3. **Party Over Spiral**: Chaotic spiral with lighter bodies
4. **Sun Dance**: Three-body gravitational dance
5. **Square Grid**: Bodies arranged in a grid pattern
6. **Body Circle**: Bodies arranged in circular formation

## Technical Details

### Vulkan Backend
The application uses Veldrid's Vulkan backend, which provides:
- Modern GPU features and optimizations
- Lower CPU overhead compared to older APIs
- Better multi-threading support
- Cross-platform compatibility

### Shader Pipeline
- Vertex Shader: Transforms positions using orthographic projection
- Fragment Shader: Samples textures for planet rendering
- GLSL 450 shaders compiled to SPIRV at runtime

### Performance
- Procedural texture generation for planets/sun
- Dynamic vertex buffer updates each frame
- Resource pooling for rendering efficiency

## Compatibility Note
This version requires a Vulkan-compatible GPU and drivers. If Vulkan is not available, Veldrid can be configured to use other backends (Direct3D 11, OpenGL, or Metal) by changing the `GraphicsBackend` parameter in `Game1.Run()`.

To use a different backend, modify this line in Game1.cs:
```csharp
GraphicsBackend.Vulkan  // Change to: Direct3D11, OpenGL, or Metal
```
"# SolarSystem" 
